using System.Text.Json;
using System.Threading.RateLimiting;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Zilean.Database.Extensions;
using Zilean.Shared.Features.Ingestion;
using Zilean.Shared.Features.Utilities;

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
        client.DefaultRequestHeaders.Add("X-Api-Key", configuration.Prowlarr.ApiKey);
        return _prowlarrClient = client;
    }

    private string BuildNativeSearchUrl(ProwlarrIndexer indexer, string query, int offset)
    {
        return $"{configuration.Prowlarr.BaseUrl.TrimEnd('/')}/api/v1/search"
            + $"?query={Uri.EscapeDataString(query)}"
            + $"&indexerIds={indexer.IndexerId}"
            + $"&categories={indexer.Categories}"
            + $"&type=search"
            + $"&limit={PageSize}"
            + $"&offset={offset}";
    }

    private List<TorrentInfo> ParseNativeResponse(string json, string sourceName)
    {
        var torrents = new List<TorrentInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("infoHash", out var ih) ||
                    !item.TryGetProperty("title", out var tl))
                    continue;

                var infoHash = ih.GetString();
                var title = tl.GetString();
                var size = item.TryGetProperty("size", out var sz) ? sz.GetInt64().ToString() : null;
                var pubDateStr = item.TryGetProperty("publishDate", out var pd) ? pd.GetString() : null;

                if (string.IsNullOrWhiteSpace(infoHash) || string.IsNullOrWhiteSpace(title))
                    continue;

                DateTime ingestedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(pubDateStr) && DateTime.TryParse(pubDateStr, out var parsedDate))
                {
                    ingestedAt = parsedDate.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc)
                        : parsedDate.ToUniversalTime();
                }

                var categories = item.TryGetProperty("categories", out var cats) ? cats : default;
                var torrent = new TorrentInfo
                {
                    InfoHash = infoHash.ToLowerInvariant(),
                    RawTitle = title,
                    ParsedTitle = title,
                    CleanedParsedTitle = Parsing.CleanQuery(title),
                    NormalizedTitle = title.ToLowerInvariant(),
                    Resolution = string.Empty,
                    Size = size,
                    IngestedAt = ingestedAt,
                    Source = sourceName,
                    Torrent = true,
                    Category = ParseNativeCategories(categories),
                };

                torrents.Add(torrent);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Prowlarr] Failed to parse native API response for {SourceName}", sourceName);
        }

        return torrents;
    }

    private static string ParseNativeCategories(JsonElement categories)
    {
        if (categories.ValueKind != JsonValueKind.Array) return "other";
        foreach (var cat in categories.EnumerateArray())
        {
            var id = cat.TryGetProperty("id", out var cid) ? cid.GetInt32() : 0;
            if (id / 1000 == 5) return "tvSeries";
            if (id == 2000) return "movie";
        }
        return "other";
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

    private async Task<int> SyncIndexerAsync(ProwlarrIndexer indexer, bool backfillMode = false)
    {
        var stats = await GetOrCreateStatsAsync(indexer.SourceName);
        var lastSyncAt = stats.LastSyncAt;

        var client = GetProwlarrClient();
        var pipeline = GetProwlarrPipeline();

        var offset = 0;
        var totalProcessed = 0;
        var page = 0;
        DateTime? maxPubDate = null;
        var logPrefix = backfillMode ? "[ProwlarrBackfill]" : "[ProwlarrSync]";

        while (!CancellationToken.IsCancellationRequested)
        {
            page++;
            var url = BuildNativeSearchUrl(indexer, "", offset);

            var response = await pipeline.ExecuteAsync(
                async ct => await client.GetAsync(url, ct),
                CancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(CancellationToken);
            var torrents = ParseNativeResponse(content, indexer.SourceName);

            if (torrents.Count == 0)
            {
                logger.LogInformation("{Prefix} {SourceName} page {Page}: 0 torrents (end of feed)",
                    logPrefix, indexer.SourceName, page);
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

                logger.LogInformation("{Prefix} {SourceName} page {Page}: {Count} torrents",
                    logPrefix, indexer.SourceName, page, itemsToProcess.Count);
            }
            else
            {
                logger.LogInformation("{Prefix} {SourceName} page {Page}: 0 new torrents",
                    logPrefix, indexer.SourceName, page);

                if (!backfillMode)
                {
                    break;
                }
            }

            offset += PageSize;

            if (torrents.Count < PageSize)
            {
                logger.LogInformation("{Prefix} {SourceName} page {Page}: last page ({Count} items)",
                    logPrefix, indexer.SourceName, page, torrents.Count);
                break;
            }

            if (backfillMode && maxPubDate.HasValue && maxPubDate.Value > lastSyncAt)
            {
                stats.LastSyncAt = maxPubDate.Value;
            }

            var delayMs = backfillMode ? 5000 : 1000;
            await Task.Delay(delayMs, CancellationToken);
        }

        if (backfillMode && maxPubDate.HasValue && maxPubDate.Value > lastSyncAt)
        {
            stats.LastSyncAt = maxPubDate.Value;
        }

        stats.TorrentCount += totalProcessed;
        stats.LastError = null;
        await dbContext.SaveChangesAsync(CancellationToken);

        return totalProcessed;
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

        logger.LogInformation("[ProwlarrBackfill] Starting broad backfill for '{SourceName}' from untilDate {UntilDate}",
            sourceName, untilDate.ToString("yyyy-MM-dd"));

        var count = await SyncIndexerAsync(indexer, backfillMode: true);

        logger.LogInformation("[ProwlarrBackfill] Complete for '{SourceName}': {Total} torrents",
            sourceName, count);

        return count;
    }

    public async Task<int> BackfillFromImdbTitlesAsync()
    {
        var totalProcessed = 0;
        var totalQueried = 0;
        var totalSkipped = 0;

        var indexerPriority = configuration.Prowlarr.Indexers
            .Where(i => i.Enabled && !string.IsNullOrWhiteSpace(i.SourceName))
            .ToList();

        if (indexerPriority.Count == 0)
        {
            logger.LogWarning("[ImdbBackfill] No enabled Prowlarr indexers, skipping");
            return 0;
        }

        logger.LogInformation("[ImdbBackfill] Starting IMDb-driven backfill with {Count} indexers",
            indexerPriority.Count);

        var client = GetProwlarrClient();
        var pipeline = GetProwlarrPipeline();

        var titles = await dbContext.ImdbFiles
            .Where(i => i.Category == "movie" || i.Category == "tvSeries")
            .OrderBy(i => i.Year)
            .Select(i => new { i.Title, i.OriginalTitle, i.Category, i.ImdbId, i.Year })
            .ToListAsync(CancellationToken);

        logger.LogInformation("[ImdbBackfill] Loaded {Count} IMDb titles to check", titles.Count);

        foreach (var imdbEntry in titles)
        {
            if (CancellationToken.IsCancellationRequested) break;

            // Prefer original title (e.g. "Shingeki no Kyojin") over English title (e.g. "Attack on Titan")
            var titlesToTry = new List<string>();
            if (!string.IsNullOrWhiteSpace(imdbEntry.OriginalTitle) &&
                imdbEntry.OriginalTitle != imdbEntry.Title)
            {
                titlesToTry.Add(imdbEntry.OriginalTitle);
                titlesToTry.Add(imdbEntry.Title);
            }
            else if (!string.IsNullOrWhiteSpace(imdbEntry.Title))
            {
                titlesToTry.Add(imdbEntry.Title);
            }
            if (titlesToTry.Count == 0) continue;

            foreach (var baseTitle in titlesToTry)
            {
            // Check if this title already has torrents in the DB (DMM hashlist or Prowlarr)
            var existsInDb = await dbContext.Torrents
                .FromSqlRaw("""
                    SELECT * FROM "Torrents"
                    WHERE "CleanedParsedTitle" IS NOT NULL
                    AND similarity("CleanedParsedTitle", {0}) > 0.5
                    LIMIT 1
                    """, baseTitle)
                .AnyAsync(CancellationToken);

            if (existsInDb)
            {
                totalSkipped++;
                if (totalSkipped % 100 == 0)
                {
                    logger.LogInformation("[ImdbBackfill] Skipped {Skipped} existing, queried {Queried}, found {Found} torrents",
                        totalSkipped, totalQueried, totalProcessed);
                }
                continue;
            }

            // For TV shows, search per-season/episode for complete coverage
            var searchQueries = new List<string> { baseTitle };
            if (imdbEntry.Category == "tvSeries")
            {
                var seasonCount = await GetSeasonCountAsync(imdbEntry.ImdbId, client, pipeline);
                for (var s = 1; s <= seasonCount && searchQueries.Count < 200; s++)
                {
                    var episodeCount = await GetEpisodeCountAsync(imdbEntry.ImdbId, s, client, pipeline);
                    for (var e = 1; e <= episodeCount; e++)
                        searchQueries.Add($"{baseTitle} S{s:D2}E{e:D2}");
                }
            }

            // Paginate through results for this title
            var allTorrents = new List<TorrentInfo>();
            var foundTorrents = 0;
            try
            {
                foreach (var searchQuery in searchQueries)
                {
                    // Skip if this exact episode/season is already cached
                    if (searchQuery != baseTitle)
                    {
                        var epExists = await dbContext.Torrents
                            .FromSqlRaw("""
                                SELECT * FROM "Torrents"
                                WHERE "CleanedParsedTitle" IS NOT NULL
                                AND "CleanedParsedTitle" ILIKE '%' || {0} || '%'
                                LIMIT 1
                                """, searchQuery)
                            .AnyAsync(CancellationToken);

                        if (epExists)
                        {
                            totalSkipped++;
                            continue;
                        }
                    }
                    var offset = 0;
                    var maxPages = 2;
                    for (var p = 0; p < maxPages; p++)
                    {
                        var url = $"{configuration.Prowlarr.BaseUrl.TrimEnd('/')}/api/v1/search"
                            + $"?query={Uri.EscapeDataString(searchQuery)}"
                            + $"&type=search"
                            + $"&limit={PageSize}"
                            + $"&offset={offset}";

                        var response = await pipeline.ExecuteAsync(
                            async ct => await client.GetAsync(url, ct),
                            CancellationToken);

                        if (!response.IsSuccessStatusCode) break;

                        totalQueried++;
                        var content = await response.Content.ReadAsStringAsync(CancellationToken);
                        var pageTorrents = ParseNativeResponse(content, "prowlarr");

                        if (pageTorrents.Count == 0) break;

                        allTorrents.AddRange(pageTorrents);
                        offset += PageSize;

                        if (pageTorrents.Count < PageSize) break;

                        await Task.Delay(1000, CancellationToken);
                    }

                    // For TV show season searches, stop when a season returns 0 results (no more seasons)
                    if (searchQuery != baseTitle && allTorrents.Count == foundTorrents) break;
                    foundTorrents = allTorrents.Count;
                }

                if (allTorrents.Count > 0)
                {
                    allTorrents = allTorrents
                        .OrderByDescending(t => long.TryParse(t.Size, out var s) ? s : 0)
                        .ToList();

                    await dbContext.UpsertTorrentsAsync(allTorrents, "prowlarr", CancellationToken);
                    totalProcessed += allTorrents.Count;
                    foundTorrents = allTorrents.Count;

                    logger.LogInformation("[ImdbBackfill] '{Title}' ({Category}, {Year}) → {Count} torrents ({Searches} searches)",
                        baseTitle, imdbEntry.Category, imdbEntry.Year, allTorrents.Count, totalQueried > 0 ? 1 : 0);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ImdbBackfill] Failed to query for '{Title}'", baseTitle);
            }

            if (foundTorrents > 0) break;
        }

            // 5s between titles to avoid rate limiting
            await Task.Delay(5000, CancellationToken);

            if (totalQueried % 100 == 0)
            {
                logger.LogInformation("[ImdbBackfill] Progress: {Queried} queried, {Found} torrents, {Skipped} skipped",
                    totalQueried, totalProcessed, totalSkipped);
            }
        }

        logger.LogInformation("[ImdbBackfill] Complete: {Total} torrents from {Queried} queries ({Skipped} existing titles skipped)",
            totalProcessed, totalQueried, totalSkipped);

        return totalProcessed;
    }

    private async Task<int> GetSeasonCountAsync(string imdbId, HttpClient client, ResiliencePipeline<HttpResponseMessage> pipeline)
    {
        try
        {
            var url = $"https://api.imdbapi.dev/titles/{imdbId}/seasons";
            var response = await pipeline.ExecuteAsync(async ct => await client.GetAsync(url, ct), CancellationToken);
            if (!response.IsSuccessStatusCode) return 1;
            var json = await response.Content.ReadAsStringAsync(CancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("seasons", out var seasons))
                return Math.Max(1, seasons.GetArrayLength());
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[ImdbBackfill] Failed to get seasons for {ImdbId}", imdbId);
        }
        return 1;
    }

    private async Task<int> GetEpisodeCountAsync(string imdbId, int season, HttpClient client, ResiliencePipeline<HttpResponseMessage> pipeline)
    {
        try
        {
            var url = $"https://api.imdbapi.dev/titles/{imdbId}/episodes?season={season}";
            var response = await pipeline.ExecuteAsync(async ct => await client.GetAsync(url, ct), CancellationToken);
            if (!response.IsSuccessStatusCode) return 1;
            var json = await response.Content.ReadAsStringAsync(CancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("episodes", out var episodes))
                return Math.Max(1, episodes.GetArrayLength());
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[ImdbBackfill] Failed to get episodes for {ImdbId} S{Season}", imdbId, season);
        }
        return 1;
    }
}
