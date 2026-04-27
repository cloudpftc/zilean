using System.Threading.RateLimiting;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using System.Xml.Linq;
using Zilean.Database.Extensions;
using Zilean.Shared.Features.Ingestion;

namespace Zilean.ApiService.Features.Sync;

public class ProwlarrSyncJob(
    ILogger<ProwlarrSyncJob> logger,
    ZileanDbContext dbContext,
    ZileanConfiguration configuration) : IInvocable, ICancellableInvocable
{
    public CancellationToken CancellationToken { get; set; }
    private const int PageSize = 100;

    private ResiliencePipeline<HttpResponseMessage>? _prowlarrPipeline;
    private HttpClient? _prowlarrClient;

    private ResiliencePipeline<HttpResponseMessage> GetProwlarrPipeline()
    {
        return _prowlarrPipeline ??= new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRateLimiter(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 1,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromSeconds(3),
                QueueLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }))
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromMinutes(5),
                OnTimeout = args =>
                {
                    logger.LogWarning("[ProwlarrResilience] Request timed out after {Timeout}s", args.Timeout.TotalSeconds);
                    return default;
                },
            })
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = args => args.Outcome.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? PredicateResult.True()
                    : PredicateResult.False(),
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(3),
                MaxDelay = TimeSpan.FromSeconds(30),
                UseJitter = true,
                OnRetry = args =>
                {
                    logger.LogWarning("[ProwlarrResilience] Retry {Attempt}/{Max} after {Delay}s (429 rate limited)",
                        args.AttemptNumber + 1, 5, args.RetryDelay.TotalSeconds);
                    return default;
                },
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = args => args.Outcome.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? PredicateResult.True()
                    : PredicateResult.False(),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(60),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                OnOpened = args =>
                {
                    logger.LogWarning("[ProwlarrResilience] Circuit BREAKER OPEN — too many 429s, pausing {Break}s",
                        args.BreakDuration.TotalSeconds);
                    return default;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("[ProwlarrResilience] Circuit breaker closed — resuming");
                    return default;
                },
                OnHalfOpened = _ => default,
            })
            .Build();
    }

    private static readonly SocketsHttpHandler _prowlarrHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        EnableMultipleHttp2Connections = true,
    };

    private HttpClient GetProwlarrClient()
    {
        if (_prowlarrClient is not null) return _prowlarrClient;
        var client = new HttpClient(_prowlarrHandler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Zilean/2.0");
        return _prowlarrClient = client;
    }

    public async Task Invoke()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            logger.LogInformation("[ProwlarrSync] Starting Prowlarr sync job");

            if (!configuration.Prowlarr.Enabled)
            {
                logger.LogInformation("[ProwlarrSync] Prowlarr is disabled, skipping");
                return;
            }

            var enabledIndexers = configuration.Prowlarr.Indexers
                .Where(i => i.Enabled && !string.IsNullOrWhiteSpace(i.SourceName))
                .ToList();

            if (enabledIndexers.Count == 0)
            {
                logger.LogInformation("[ProwlarrSync] No enabled Prowlarr indexers found, skipping");
                return;
            }

            var totalTorrents = 0;
            var indexerCount = 0;

            foreach (var indexer in enabledIndexers)
            {
                try
                {
                    var count = await SyncIndexerAsync(indexer);
                    totalTorrents += count;
                    indexerCount++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[ProwlarrSync] Failed to sync indexer '{SourceName}'", indexer.SourceName);
                    await SaveIndexerErrorAsync(indexer.SourceName, ex.Message);
                }
            }

            sw.Stop();
            logger.LogInformation(
                "[ProwlarrSync] Complete: {Total} torrents from {IndexerCount} indexers in {ElapsedMs}ms",
                totalTorrents, indexerCount, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[ProwlarrSync] Prowlarr sync job failed");
            throw;
        }
    }

    public async Task<int> SyncSingleIndexerAsync(string sourceName)
    {
        var indexer = configuration.Prowlarr.Indexers
            .FirstOrDefault(i => i.SourceName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));

        if (indexer == null)
        {
            throw new InvalidOperationException($"Indexer '{sourceName}' not found in configuration.");
        }

        logger.LogInformation("[ProwlarrSync] Manually triggering sync for indexer '{SourceName}'", sourceName);
        return await SyncIndexerAsync(indexer);
    }

    private async Task<int> SyncIndexerAsync(ProwlarrIndexer indexer)
    {
        var stats = await GetOrCreateStatsAsync(indexer.SourceName);
        var lastSyncAt = stats.LastSyncAt;

        var client = GetProwlarrClient();
        var pipeline = GetProwlarrPipeline();

        var offset = 0;
        var totalProcessed = 0;
        var page = 0;
        DateTime? maxPubDate = null;

        while (!CancellationToken.IsCancellationRequested)
        {
            page++;
            var url = $"{configuration.Prowlarr.BaseUrl.TrimEnd('/')}/{indexer.IndexerId}/api"
                + $"?t=search&apikey={configuration.Prowlarr.ApiKey}"
                + $"&cat={indexer.Categories}&extended=1"
                + $"&offset={offset}&limit={PageSize}";

            var response = await pipeline.ExecuteAsync(
                async ct => await client.GetAsync(url, ct),
                CancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(CancellationToken);
            var torrents = ParseRssFeed(content, indexer.SourceName, lastSyncAt);

            if (torrents.Count == 0)
            {
                logger.LogInformation("[ProwlarrSync] {SourceName} page {Page}: 0 torrents (end of feed)", indexer.SourceName, page);
                break;
            }

            if (torrents.Count < PageSize)
            {
                logger.LogInformation("[ProwlarrSync] {SourceName} page {Page}: {Count} torrents (last page)", indexer.SourceName, page, torrents.Count);
            }

            var allOlderOrEqual = torrents.All(t => t.IngestedAt <= lastSyncAt);
            if (allOlderOrEqual)
            {
                break;
            }

            var newItems = torrents.Where(t => t.IngestedAt > lastSyncAt).ToList();
            if (newItems.Count > 0)
            {
                await dbContext.UpsertTorrentsAsync(newItems, indexer.SourceName, CancellationToken);
                totalProcessed += newItems.Count;

                var pageMaxDate = newItems.Max(t => t.IngestedAt);
                if (maxPubDate == null || pageMaxDate > maxPubDate)
                {
                    maxPubDate = pageMaxDate;
                }

                logger.LogInformation("[ProwlarrSync] {SourceName} page {Page}: {Count} torrents", indexer.SourceName, page, newItems.Count);
            }
            else
            {
                logger.LogInformation("[ProwlarrSync] {SourceName} page {Page}: 0 new torrents (all at or before checkpoint)", indexer.SourceName, page);
            }

            offset += PageSize;

            // Respect Prowlarr rate limits: 1s delay between pages
            await Task.Delay(1000, CancellationToken);
        }

        stats.LastSyncAt = maxPubDate ?? stats.LastSyncAt;
        stats.TorrentCount += totalProcessed;
        stats.LastError = null;
        await dbContext.SaveChangesAsync(CancellationToken);

        return totalProcessed;
    }

    private List<TorrentInfo> ParseRssFeed(string xmlContent, string sourceName, DateTime lastSyncAt)
    {
        var torrents = new List<TorrentInfo>();

        try
        {
            var doc = XDocument.Parse(xmlContent);
            var items = doc.Descendants("item").ToList();

            foreach (var item in items)
            {
                var infoHash = GetTorznabAttr(item, "infohash");
                if (string.IsNullOrWhiteSpace(infoHash))
                {
                    continue;
                }

                var title = item.Element("title")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var size = item.Element("size")?.Value;
                var pubDateStr = item.Element("pubDate")?.Value;

                DateTime ingestedAt;
                if (!string.IsNullOrWhiteSpace(pubDateStr) && DateTime.TryParse(pubDateStr, out var parsedDate))
                {
                    ingestedAt = parsedDate.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc)
                        : parsedDate.ToUniversalTime();
                }
                else
                {
                    ingestedAt = DateTime.UtcNow;
                }

                var torrent = new TorrentInfo
                {
                    InfoHash = infoHash.ToLowerInvariant(),
                    RawTitle = title,
                    ParsedTitle = title,
                    CleanedParsedTitle = title,
                    NormalizedTitle = title.ToLowerInvariant(),
                    Resolution = string.Empty,
                    Size = size,
                    IngestedAt = ingestedAt,
                    Source = sourceName,
                    Torrent = true,
                    Category = "other",
                };

                torrents.Add(torrent);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProwlarrSync] Failed to parse RSS feed for {SourceName}", sourceName);
        }

        return torrents;
    }

    private static string? GetTorznabAttr(XElement item, string attrName)
    {
        return item.Elements()
            .Where(e => e.Name.LocalName == "attr" && e.Name.NamespaceName.Contains("torznab"))
            .FirstOrDefault(e => e.Attribute("name")?.Value == attrName)
            ?.Attribute("value")
            ?.Value;
    }

    private async Task<TorrentSourceStats> GetOrCreateStatsAsync(string sourceName)
    {
        var stats = await dbContext.TorrentSourceStats
            .FirstOrDefaultAsync(s => s.Source == sourceName, CancellationToken);

        if (stats == null)
        {
            stats = new TorrentSourceStats
            {
                Source = sourceName,
                LastSyncAt = DateTime.UnixEpoch,
                TorrentCount = 0,
            };
            dbContext.TorrentSourceStats.Add(stats);
            await dbContext.SaveChangesAsync(CancellationToken);
        }

        return stats;
    }

    private async Task SaveIndexerErrorAsync(string sourceName, string errorMessage)
    {
        try
        {
            var stats = await GetOrCreateStatsAsync(sourceName);
            stats.LastError = errorMessage;
            await dbContext.SaveChangesAsync(CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProwlarrSync] Failed to save error stats for {SourceName}", sourceName);
        }
    }

    private static readonly string[] _backfillKeywords =
    [
        "2000","2001","2002","2003","2004","2005","2006","2007","2008","2009",
        "2010","2011","2012","2013","2014","2015","2016","2017","2018","2019",
        "2020","2021","2022","2023","2024","2025","2026",
        "1","2","3","4","5",
        "1080p","2160p","BluRay","WEB-DL","HEVC","H264","x264","x265","the","and",
        "a","b","c","d","e","f","g","h","i","j","k","l","m","n","o","p","q","r","s","t","u","v","w","x","y","z"
    ];

    public async Task<int> BackfillIndexerAsync(string sourceName, DateTime untilDate)
    {
        var indexer = configuration.Prowlarr.Indexers
            .FirstOrDefault(i => i.SourceName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));

        if (indexer == null)
        {
            throw new InvalidOperationException($"Indexer '{sourceName}' not found.");
        }

        var stats = await GetOrCreateStatsAsync(sourceName);
        stats.LastSyncAt = untilDate;
        await dbContext.SaveChangesAsync(CancellationToken);

        logger.LogInformation("[ProwlarrBackfill] Starting keyword backfill for '{SourceName}' with untilDate {UntilDate}",
            sourceName, untilDate.ToString("yyyy-MM-dd"));

        var totalProcessed = 0;
        foreach (var keyword in _backfillKeywords)
        {
            if (CancellationToken.IsCancellationRequested)
            {
                break;
            }

            var count = await SyncIndexerWithQueryAsync(indexer, keyword, backfillMode: true);
            totalProcessed += count;

            await Task.Delay(5000, CancellationToken);
        }

        logger.LogInformation("[ProwlarrBackfill] Complete for '{SourceName}': {Total} torrents from {Keywords} keywords",
            sourceName, totalProcessed, _backfillKeywords.Length);

        return totalProcessed;
    }

    private async Task<int> SyncIndexerWithQueryAsync(ProwlarrIndexer indexer, string query, bool backfillMode = false)
    {
        var stats = await GetOrCreateStatsAsync(indexer.SourceName);
        var lastSyncAt = stats.LastSyncAt;

        var client = GetProwlarrClient();
        var pipeline = GetProwlarrPipeline();

        var offset = 0;
        var totalProcessed = 0;
        var page = 0;
        DateTime? maxPubDate = null;

        while (!CancellationToken.IsCancellationRequested)
        {
            page++;
            var url = $"{configuration.Prowlarr.BaseUrl.TrimEnd('/')}/{indexer.IndexerId}/api"
                + $"?t=search&apikey={configuration.Prowlarr.ApiKey}"
                + $"&cat={indexer.Categories}&extended=1"
                + $"&offset={offset}&limit={PageSize}"
                + $"&q={Uri.EscapeDataString(query)}";

            var response = await pipeline.ExecuteAsync(
                async ct => await client.GetAsync(url, ct),
                CancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(CancellationToken);
            var torrents = ParseRssFeed(content, indexer.SourceName, lastSyncAt);

            if (torrents.Count == 0)
            {
                logger.LogInformation("[ProwlarrBackfill] {SourceName} query '{Query}' page {Page}: 0 torrents",
                    indexer.SourceName, query, page);
                break;
            }

            var itemsToProcess = backfillMode
                ? torrents
                : torrents.Where(t => t.IngestedAt > lastSyncAt).ToList();

            if (itemsToProcess.Count > 0)
            {
                await dbContext.UpsertTorrentsAsync(itemsToProcess, indexer.SourceName, CancellationToken);
                totalProcessed += itemsToProcess.Count;

                var pageMaxDate = itemsToProcess.Max(t => t.IngestedAt);
                if (maxPubDate == null || pageMaxDate > maxPubDate)
                {
                    maxPubDate = pageMaxDate;
                }

                logger.LogInformation("[ProwlarrBackfill] {SourceName} query '{Query}' page {Page}: {Count} torrents",
                    indexer.SourceName, query, page, itemsToProcess.Count);
            }
            else
            {
                logger.LogInformation("[ProwlarrBackfill] {SourceName} query '{Query}' page {Page}: 0 new torrents",
                    indexer.SourceName, query, page);

                if (!backfillMode)
                {
                    break;
                }
            }

            offset += PageSize;

            if (torrents.Count < PageSize)
            {
                logger.LogInformation("[ProwlarrBackfill] {SourceName} query '{Query}' page {Page}: last page ({Count} items)",
                    indexer.SourceName, query, page, torrents.Count);
                break;
            }

            var delayMs = backfillMode ? 5000 : 1000;
            await Task.Delay(delayMs, CancellationToken);
        }

        if (backfillMode && maxPubDate.HasValue)
        {
            stats.LastSyncAt = maxPubDate.Value;
            await dbContext.SaveChangesAsync(CancellationToken);
        }

        return totalProcessed;
    }
}
