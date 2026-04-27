using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Zilean.Tests.Features.Search;

/// <summary>
/// Integration tests replicating Comet's /dmm/filtered search behavior.
/// Comet sends title-only for movies, title+season+episode as separate params for series.
/// </summary>
public class DmmSearchIntegrationTests
{
    private readonly HttpClient _client;
    private const string BaseUrl = "http://localhost:8181";
    private const string ApiKey = "test-api-key-123";

    public DmmSearchIntegrationTests()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    private static async Task<JsonElement[]> GetResultsAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement[]>(json);
    }

    // ── Comet movie query: /dmm/filtered?query={title} ──

    [Fact]
    public async Task FilteredSearch_MovieTitleOnly_ReturnsResults()
    {
        var response = await _client.GetAsync($"{BaseUrl}/dmm/filtered?query=Dorohedoro");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FilteredSearch_MovieTitleOnly_ContainsRequiredFields()
    {
        var response = await _client.GetAsync($"{BaseUrl}/dmm/filtered?query=Dorohedoro");
        var results = await GetResultsAsync(response);

        if (results.Length > 0)
        {
            var first = results[0];
            first.TryGetProperty("raw_title", out _).Should().BeTrue("Comet reads raw_title");
            first.TryGetProperty("info_hash", out _).Should().BeTrue("Comet reads info_hash");
            first.TryGetProperty("size", out _).Should().BeTrue("Comet reads size");
        }
    }

    // ── Comet series query: /dmm/filtered?query={title}&season=N&episode=N ──

    [Fact]
    public async Task FilteredSearch_SeriesWithSeasonEpisode_ReturnsOk()
    {
        var response = await _client.GetAsync(
            $"{BaseUrl}/dmm/filtered?query=Dorohedoro%20Season%202&season=2&episode=6");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FilteredSearch_SeriesWithSeasonEpisode_ContainsRequiredFields()
    {
        var response = await _client.GetAsync(
            $"{BaseUrl}/dmm/filtered?query=Ganbare%20Nakamura&season=1&episode=5");
        var results = await GetResultsAsync(response);

        if (results.Length > 0)
        {
            results[0].TryGetProperty("raw_title", out _).Should().BeTrue();
            results[0].TryGetProperty("info_hash", out _).Should().BeTrue();
            results[0].TryGetProperty("size", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task DmmSearch_ReturnsNyAA_SourcedTorrents()
    {
        var response = await _client.PostAsync($"{BaseUrl}/dmm/search",
            JsonContent.Create(new { queryText = "SubsPlease Ganbare Nakamura" }));
        var results = await GetResultsAsync(response);

        var nyaaResults = results.Where(r =>
            r.TryGetProperty("source", out var src) && src.GetString() == "nyaa").ToList();

        nyaaResults.Should().NotBeEmpty("Backfilled nyaa torrents should appear in search results");
    }

    // ── Comet doesn't use /dmm/search, but verify it returns the same format ──

    [Fact]
    public async Task UnfilteredSearch_ReturnsRequiredFields()
    {
        var response = await _client.PostAsync($"{BaseUrl}/dmm/search",
            JsonContent.Create(new { queryText = "Dorohedoro" }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await GetResultsAsync(response);
        results.Should().NotBeNullOrEmpty();
        results.First().TryGetProperty("raw_title", out _).Should().BeTrue();
        results.First().TryGetProperty("info_hash", out _).Should().BeTrue();
        results.First().TryGetProperty("size", out _).Should().BeTrue();
    }

    // ── Backfill endpoint fire-and-forget ──

    [Fact]
    public async Task BackfillEndpoint_ReturnsOkImmediately()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/admin/sources/backfill/nyaa?untilDate=2025-01-01");
        request.Headers.Add("X-API-Key", ApiKey);

        var sw = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, "Fire-and-forget should return immediately");
    }

    [Fact]
    public async Task BackfillEndpoint_InvalidDate_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/admin/sources/backfill/nyaa?untilDate=not-a-date");
        request.Headers.Add("X-API-Key", ApiKey);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BackfillEndpoint_MissingDate_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/admin/sources/backfill/nyaa");
        request.Headers.Add("X-API-Key", ApiKey);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Backfill-all endpoint ──

    [Fact]
    public async Task BackfillAllEndpoint_ReturnsOkImmediately()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/admin/sources/backfill-all?untilDate=2020-01-01");
        request.Headers.Add("X-API-Key", ApiKey);

        var sw = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, "Fire-and-forget should return immediately");
    }

    [Fact]
    public async Task BackfillAllEndpoint_ReturnsSourceList()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/admin/sources/backfill-all?untilDate=2020-01-01");
        request.Headers.Add("X-API-Key", ApiKey);

        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(json).RootElement;

        body.TryGetProperty("sources", out _).Should().BeTrue();
        body.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetString().Should().Be("running");
    }

    // ── On-demand scrape fire-and-forget ──

    [Fact]
    public async Task OnDemandScrape_ReturnsOkImmediately()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/dmm/on-demand-scrape");
        request.Headers.Add("X-API-Key", ApiKey);

        var sw = Stopwatch.StartNew();
        var response = await _client.SendAsync(request);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, "Fire-and-forget should return immediately");
    }

    // ── IMDb search (also used by Comet) ──

    [Fact]
    public async Task ImdbSearch_ReturnsResults()
    {
        var response = await _client.PostAsync(
            $"{BaseUrl}/imdb/search?query=Batman&year=2022", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Health check ──

    [Fact]
    public async Task HealthCheck_ReturnsPong()
    {
        var response = await _client.GetAsync($"{BaseUrl}/healthchecks/ping");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Pong");
    }
}
