using System.Net;
using System.Xml.Linq;
using Coravel.Invocable;
using Microsoft.EntityFrameworkCore;
using Zilean.ApiService.Features.Sync;
using Zilean.Database;
using Zilean.Shared.Features.Ingestion;

namespace Zilean.Tests.Features.Sync;

[Collection("ProwlarrSyncJobTests")]
public class ProwlarrSyncJobTests : IAsyncLifetime
{
    private readonly PostgresLifecycleFixture _fixture;
    private ZileanDbContext _dbContext = null!;
    private MockHttpHandler _handler = null!;
    private HttpClient _httpClient = null!;
    private IHttpClientFactory _httpClientFactory = null!;
    private ILogger<ProwlarrSyncJob> _logger = null!;

    public ProwlarrSyncJobTests(PostgresLifecycleFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ZileanDbContext>();
        optionsBuilder.UseNpgsql(_fixture.ZileanConfiguration.Database.ConnectionString);
        _dbContext = new ZileanDbContext(optionsBuilder.Options);
        await _dbContext.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm");
        await _dbContext.Database.EnsureCreatedAsync();

        _handler = new MockHttpHandler();
        _httpClient = new HttpClient(_handler);
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _httpClientFactory.CreateClient("Prowlarr").Returns(_httpClient);
        _logger = Substitute.For<ILogger<ProwlarrSyncJob>>();
    }

    public async Task DisposeAsync()
    {
        _httpClient.Dispose();
        _fixture.ZileanConfiguration.Prowlarr = new ProwlarrConfiguration();
        await _dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Torrents\" CASCADE");
        await _dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"TorrentSourceStats\" CASCADE");
        await _dbContext.DisposeAsync();
    }

    // ---------------------------------------------------------------
        // Scenario 1: Parse Torznab XML correctly
    // ---------------------------------------------------------------
    [Fact]
    public async Task SyncSingleIndexerAsync_ShouldParseTorznabXmlAndStoreCorrectFields()
    {
        // Arrange
        var xml = CreateTorznabXml(
            ("Test.Title.2026.1080p.WEB.H264-GROUP", "12345678", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2", "2026-04-26T12:00:00Z"),
            ("Another.Movie.2025.2160p.UHD.BluRay.X265-GROUP2", "87654321", "f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a", "2026-04-26T14:00:00Z")
        );
        _handler.AddOneShotResponse("/1/api", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });

        var job = CreateJobWithIndexers(EnabledIndexer(1, "test-source"));

        // Act
        var count = await job.SyncSingleIndexerAsync("test-source");

        // Assert
        _handler.CallCount.Should().Be(2);
        count.Should().Be(2);

        var stored = await _dbContext.Torrents.OrderBy(t => t.IngestedAt).ToListAsync();
        stored.Should().HaveCount(2);

        var t1 = stored[0];
        t1.InfoHash.Should().Be("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2");
        t1.RawTitle.Should().Be("Test.Title.2026.1080p.WEB.H264-GROUP");
        t1.Size.Should().Be("12345678");
        t1.Source.Should().Be("test-source");

        var t2 = stored[1];
        t2.InfoHash.Should().Be("f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a");
        t2.RawTitle.Should().Be("Another.Movie.2025.2160p.UHD.BluRay.X265-GROUP2");
        t2.Size.Should().Be("87654321");
        t2.Source.Should().Be("test-source");
    }

    // ---------------------------------------------------------------
    // Scenario 2: Disabled indexer skip
    // ---------------------------------------------------------------
    [Fact]
    public async Task Invoke_ShouldSkipDisabledIndexers()
    {
        // Arrange
        _handler.AddOneShotResponse("/1/api", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CreateTorznabXml(("A", "1", "aaabbbcccdddeeefff001aaabbbcccdddeeefff00", "2026-04-26T12:00:00Z"))) });
        _handler.AddOneShotResponse("/2/api", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CreateTorznabXml(("B", "2", "bbbaaacccddddeeefff002bbbaaacccddddeeefff01", "2026-04-26T12:00:00Z"))) });

        var job = CreateJobWithIndexers(
            EnabledIndexer(1, "enabled-one"),
            DisabledIndexer(2, "disabled-one")
        );

        // Act
        await job.Invoke();

        // Assert
        _handler.CallCount.Should().Be(2);

        var stored = await _dbContext.Torrents.ToListAsync();
        stored.Should().ContainSingle(t => t.Source == "enabled-one");
        stored.Should().NotContain(t => t.Source == "disabled-one");
    }

    // ---------------------------------------------------------------
    // Scenario 3: Empty results
    // ---------------------------------------------------------------
    [Fact]
    public async Task SyncSingleIndexerAsync_ShouldHandleEmptyResultsGracefully()
    {
        // Arrange
        var xml = CreateTorznabXml();
        _handler.AddResponse("/1/api", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });

        var job = CreateJobWithIndexers(EnabledIndexer(1, "test-source"));

        // Act
        var count = await job.SyncSingleIndexerAsync("test-source");

        // Assert
        count.Should().Be(0);

        var stored = await _dbContext.Torrents.ToListAsync();
        stored.Should().BeEmpty();

        var stats = await _dbContext.TorrentSourceStats.FirstOrDefaultAsync(s => s.Source == "test-source");
        stats.Should().NotBeNull();
        stats!.TorrentCount.Should().Be(0);
    }

    // ---------------------------------------------------------------
    // Scenario 4: HTTP error isolation (per-indexer)
    // ---------------------------------------------------------------
    [Fact]
    public async Task Invoke_ShouldIsolateIndexerErrors()
    {
        // Arrange
        _handler.AddResponse("/1/api", () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var workingXml = CreateTorznabXml(("Working.Movie.2026.1080p.WEB.H264-GROUP", "9999", "ccccddddeeeeffff00001111ccccddddeeeeffff00", "2026-04-26T12:00:00Z"));
        _handler.AddOneShotResponse("/2/api", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(workingXml) });

        var job = CreateJobWithIndexers(
            EnabledIndexer(1, "failing-indexer"),
            EnabledIndexer(2, "working-indexer")
        );

        // Act
        await job.Invoke();

        // Assert
        _handler.CallCount.Should().Be(3);

        var stored = await _dbContext.Torrents.ToListAsync();
        stored.Should().ContainSingle();
        stored[0].Source.Should().Be("working-indexer");

        var failingStats = await _dbContext.TorrentSourceStats.FirstOrDefaultAsync(s => s.Source == "failing-indexer");
        failingStats.Should().NotBeNull();
        failingStats!.LastError.Should().NotBeNullOrEmpty();
    }

    // ---------------------------------------------------------------
    // Scenario 5: Checkpoint pagination
    // ---------------------------------------------------------------
    [Fact]
    public async Task SyncSingleIndexerAsync_ShouldStopAtCheckpointAndUpdateStats()
    {
        // Arrange
        var checkpointDate = new DateTime(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);

        _dbContext.TorrentSourceStats.Add(new TorrentSourceStats
        {
            Source = "test-source",
            LastSyncAt = checkpointDate,
            TorrentCount = 0,
        });
        await _dbContext.SaveChangesAsync();

        var page1 = CreateTorznabXml(
            ("Page1.Item1", "100", "page1item1page1item1page1item1aaaa01", "2026-04-26T10:00:00Z"),
            ("Page1.Item2", "200", "page1item2page1item2page1item2aaaa02", "2026-04-26T11:00:00Z")
        );

        var page2 = CreateTorznabXml(
            ("Page2.Item1", "300", "page2item1page2item1page2item1aaaa03", "2026-04-26T11:30:00Z"),
            ("Page2.Item2", "400", "page2item2page2item2page2item2aaaa04", "2026-04-25T10:00:00Z")
        );

        var page3 = CreateTorznabXml(
            ("Page3.Item1", "500", "page3item1page3item1page3item1aaaa05", "2026-04-24T10:00:00Z"),
            ("Page3.Item2", "600", "page3item2page3item2page3item2aaaa06", "2026-04-24T11:00:00Z")
        );

        var pageNum = 0;
        _handler.AddResponse("/1/api", () =>
        {
            pageNum++;
            var xml = pageNum switch
            {
                1 => page1,
                2 => page2,
                3 => page3,
                _ => CreateTorznabXml()
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) };
        });

        var job = CreateJobWithIndexers(EnabledIndexer(1, "test-source"));

        // Act
        var count = await job.SyncSingleIndexerAsync("test-source");

        // Assert
        count.Should().Be(3);

        var stored = await _dbContext.Torrents.ToListAsync();
        stored.Should().HaveCount(3);
        stored.Should().Contain(t => t.InfoHash == "page1item1page1item1page1item1aaaa01");
        stored.Should().Contain(t => t.InfoHash == "page1item2page1item2page1item2aaaa02");
        stored.Should().Contain(t => t.InfoHash == "page2item1page2item1page2item1aaaa03");
        stored.Should().NotContain(t => t.InfoHash == "page2item2page2item2page2item2aaaa04");
        stored.Should().NotContain(t => t.InfoHash.StartsWith("page3"));

        var stats = await _dbContext.TorrentSourceStats.FirstAsync(s => s.Source == "test-source");
        stats.LastSyncAt.Should().BeCloseTo(new DateTime(2026, 4, 26, 11, 30, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
        stats.TorrentCount.Should().Be(3);
    }

    // ---------------------------------------------------------------
    // Scenario 6: Source column populated correctly
    // ---------------------------------------------------------------
    [Fact]
    public async Task SyncSingleIndexerAsync_ShouldPopulateSourceColumn()
    {
        // Arrange
        var xml = CreateTorznabXml(
            ("Nyaa.Torrent.2026.1080p.WEB.H264-GROUP", "999999", "sourcecoltestinfohashvalue00000000000001", "2026-04-26T12:00:00Z")
        );
        _handler.AddOneShotResponse("/5/api", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });

        var job = CreateJobWithIndexers(EnabledIndexer(5, "test-nyaa"));

        // Act
        var count = await job.SyncSingleIndexerAsync("test-nyaa");

        // Assert
        count.Should().Be(1);

        var stored = await _dbContext.Torrents.FirstAsync();
        stored.Source.Should().Be("test-nyaa");
    }

    // ---------------------------------------------------------------
    // Scenario 7: Sequential execution
    // ---------------------------------------------------------------
    [Fact]
    public async Task Invoke_ShouldProcessIndexersSequentially()
    {
        // Arrange
        var executionOrder = new List<string>();

        _handler.AddOneShotResponse("/10/api", () =>
        {
            executionOrder.Add("indexer-a");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CreateTorznabXml(("Seq.A", "1", "seqa00000000000000000000000000000000001", "2026-04-26T12:00:00Z")))
            };
        });

        _handler.AddOneShotResponse("/20/api", () =>
        {
            executionOrder.Add("indexer-b");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CreateTorznabXml(("Seq.B", "2", "seqb00000000000000000000000000000000002", "2026-04-26T12:00:00Z")))
            };
        });

        var job = CreateJobWithIndexers(
            EnabledIndexer(10, "indexer-a"),
            EnabledIndexer(20, "indexer-b")
        );

        // Act
        await job.Invoke();

        // Assert
        executionOrder.Should().HaveCount(2);
        executionOrder[0].Should().Be("indexer-a");
        executionOrder[1].Should().Be("indexer-b");

        _handler.CallCount.Should().Be(4);

        var stored = await _dbContext.Torrents.ToListAsync();
        stored.Should().HaveCount(2);
    }

    // ---------------------------------------------------------------
    // Diagnostic: trace through SyncSingleIndexerAsync step by step
    // ---------------------------------------------------------------
    [Fact]
    public async Task SyncSingleIndexerAsync_Diagnostic()
    {
        var xml = CreateTorznabXml(
            ("Test.Title.2026.1080p.WEB.H264-GROUP", "12345678", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2", "2026-04-26T12:00:00Z")
        );
        _handler.AddOneShotResponse("/1/api", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
        var job = CreateJobWithIndexers(EnabledIndexer(1, "test-source"));

        var count = await job.SyncSingleIndexerAsync("test-source");

        _handler.CallCount.Should().Be(2);

        // Check if Upsert happened by querying DB
        var dbCount = await _dbContext.Torrents.CountAsync();
        var stats = await _dbContext.TorrentSourceStats.ToListAsync();

        count.Should().Be(1, $"dbCount={dbCount}, statsCount={stats.Count}, handlerRequests={_handler.CallCount}, url={_handler.Requests[0].RequestUri}");
    }

    // ---------------------------------------------------------------
    // XML parsing verification (isolated from job code)
    // ---------------------------------------------------------------
    [Fact]
    public void TorznabXml_ShouldParseUsingSameLogicAsJob()
    {
        var xml = CreateTorznabXml(
            ("Test.Title.2026.1080p.WEB.H264-GROUP", "12345678", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2", "2026-04-26T12:00:00Z"),
            ("Another.Movie.2025.2160p.UHD.BluRay.X265-GROUP2", "87654321", "f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a", "2026-04-26T14:00:00Z")
        );

        var doc = XDocument.Parse(xml);
        var items = doc.Descendants("item").ToList();
        items.Should().HaveCount(2);

        foreach (var item in items)
        {
            var infoHash = item.Elements()
                .Where(e => e.Name.LocalName == "attr" && e.Name.NamespaceName.Contains("torznab"))
                .FirstOrDefault(e => e.Attribute("name")?.Value == "infohash")
                ?.Attribute("value")
                ?.Value;

            infoHash.Should().NotBeNullOrEmpty();
        }

        var firstTitle = items[0].Element("title")?.Value?.Trim();
        firstTitle.Should().Be("Test.Title.2026.1080p.WEB.H264-GROUP");

        var firstSize = items[0].Element("size")?.Value;
        firstSize.Should().Be("12345678");

        var firstPubDate = items[0].Element("pubDate")?.Value;
        firstPubDate.Should().NotBeNullOrEmpty();
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private ProwlarrSyncJob CreateJobWithIndexers(params ProwlarrIndexer[] indexers)
    {
        _fixture.ZileanConfiguration.Prowlarr = new ProwlarrConfiguration
        {
            Enabled = true,
            BaseUrl = "https://prowlarr.test",
            ApiKey = "test-api-key",
            Indexers = [.. indexers],
        };

        return new ProwlarrSyncJob(_logger, _dbContext, _fixture.ZileanConfiguration)
        {
            CancellationToken = CancellationToken.None,
        };
    }

    private static ProwlarrIndexer EnabledIndexer(int id, string sourceName) =>
        new()
        {
            IndexerId = id,
            SourceName = sourceName,
            Categories = "2000,5000",
            Enabled = true,
        };

    private static ProwlarrIndexer DisabledIndexer(int id, string sourceName) =>
        new()
        {
            IndexerId = id,
            SourceName = sourceName,
            Categories = "2000,5000",
            Enabled = false,
        };

    private static string CreateTorznabXml(params (string Title, string Size, string InfoHash, string PubDate)[] items)
    {
        var ns = XNamespace.Get("http://torznab.com/schemas/rss/1.0");

        var doc = new XDocument(
            new XElement("rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "torznab", ns),
                new XElement("channel",
                    items.Select(item =>
                        new XElement("item",
                            new XElement("title", item.Title),
                            new XElement("size", item.Size),
                            new XElement("pubDate", item.PubDate),
                            new XElement(ns + "attr",
                                new XAttribute("name", "infohash"),
                                new XAttribute("value", item.InfoHash)),
                            new XElement(ns + "attr",
                                new XAttribute("name", "seeders"),
                                new XAttribute("value", "100")),
                            new XElement(ns + "attr",
                                new XAttribute("name", "peers"),
                                new XAttribute("value", "200"))
                        )
                    )
                )
            )
        );

        return doc.ToString();
    }
}

public class MockHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = new(StringComparer.Ordinal);
    private readonly List<HttpRequestMessage> _requests = [];

    public IReadOnlyList<HttpRequestMessage> Requests => _requests.AsReadOnly();
    public int CallCount => _requests.Count;

    public void AddResponse(string urlPattern, Func<HttpResponseMessage> responseFactory)
    {
        _responses[urlPattern] = responseFactory;
    }

    public void AddOneShotResponse(string urlPattern, Func<HttpResponseMessage> responseFactory)
    {
        var called = false;
        _responses[urlPattern] = () =>
        {
            if (called)
            {
                return CreateEmptyTorznabResponse();
            }
            called = true;
            return responseFactory();
        };
    }

    private static HttpResponseMessage CreateEmptyTorznabResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("<rss version=\"2.0\"><channel></channel></rss>")
        };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);

        var url = request.RequestUri?.ToString() ?? string.Empty;

        foreach (var (pattern, factory) in _responses)
        {
            if (url.Contains(pattern, StringComparison.Ordinal))
            {
                return Task.FromResult(factory());
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
