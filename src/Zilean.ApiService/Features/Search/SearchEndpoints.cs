using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using Zilean.ApiService.Features.Audit;
using Zilean.ApiService.Features.Ingestion;

namespace Zilean.ApiService.Features.Search;

public static class SearchEndpoints
{
    private const string GroupName = "dmm";
    private const string Search = "/search";
    private const string Filtered = "/filtered";
    private const string Ingest = "/on-demand-scrape";

    public static WebApplication MapDmmEndpoints(this WebApplication app, ZileanConfiguration configuration)
    {
        if (configuration.Dmm.EnableEndpoint)
        {
            app.MapGroup(GroupName)
                .WithTags(GroupName)
                .Dmm()
                .DisableAntiforgery();
        }

        return app;
    }

    private static RouteGroupBuilder Dmm(this RouteGroupBuilder group)
    {
        group.MapPost(Search, PerformSearch)
            .Produces<TorrentInfo[]>()
            .AllowAnonymous();

        group.MapGet(Filtered, PerformFilteredSearch)
            .Produces<TorrentInfo[]>()
            .AllowAnonymous();

        group.MapGet(Ingest, PerformOnDemandScrape)
            .RequireAuthorization(ApiKeyAuthentication.Policy)
            .WithMetadata(new OpenApiSecurityMetadata(ApiKeyAuthentication.Scheme));

        return group;
    }

    private static async Task PerformOnDemandScrape(HttpContext context, ILogger<GeneralInstance> logger, IShellExecutionService executionService, ILogger<DmmSyncJob> syncLogger, IMutex mutex, SyncOnDemandState state, ZileanDbContext dbContext, IFileAuditLogService fileAuditLogService, IIngestionQueueService ingestionQueueService, IQueryCacheService queryCache)
    {
        if (state.IsRunning)
        {
            logger.LogWarning("On-demand scrape already running.");
            return;
        }

        logger.LogInformation("Trying to schedule on-demand scrape with a 1 minute timeout on lock acquisition.");

        bool available = mutex.TryGetLock(nameof(DmmSyncJob), 1);

        if(available)
        {
            try
            {
                logger.LogInformation("On-demand scrape mutex lock acquired.");
                state.IsRunning = true;
                await new DmmSyncJob(executionService, syncLogger, dbContext, fileAuditLogService, ingestionQueueService, queryCache).Invoke();
            }
            finally
            {
                mutex.Release(nameof(DmmSyncJob));
                state.IsRunning = false;
            }

            return;
        }

        logger.LogWarning("Failed to acquire lock for on-demand scrape.");
    }

    private static async Task<Ok<TorrentInfo[]>> PerformSearch(HttpContext context, ITorrentInfoService torrentInfoService, ZileanConfiguration configuration, ILogger<DmmUnfilteredInstance> logger, IQueryAuditService auditService, ZileanDbContext dbContext, IQueryCacheService queryCache, [FromBody] DmmQueryRequest queryRequest)
    {
        var sw = Stopwatch.StartNew();
        var timestamp = DateTime.UtcNow;
        TorrentInfo[] results = Array.Empty<TorrentInfo>();

        try
        {
            if (string.IsNullOrEmpty(queryRequest.QueryText))
            {
                results = Array.Empty<TorrentInfo>();
                return TypedResults.Ok(results);
            }

            var cacheKey = $"search:{queryRequest.QueryText}";
            var cached = await queryCache.GetCachedAsync(cacheKey);
            if (cached != null)
            {
                logger.LogInformation("Cache hit for search query: {QueryText}", queryRequest.QueryText);
                return TypedResults.Ok(cached);
            }

            logger.LogInformation("Performing unfiltered search for {QueryText}", queryRequest.QueryText);

            results = await torrentInfoService.SearchForTorrentInfoByOnlyTitle(queryRequest.QueryText);

            logger.LogInformation("Unfiltered search for {QueryText} returned {Count} results", queryRequest.QueryText, results.Length);

            if (results.Length == 0)
            {
                try
                {
                    await TrackSearchMissAsync(dbContext, queryRequest.QueryText, null, logger);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to track search miss for query {QueryText}", queryRequest.QueryText);
                }
            }

            await queryCache.SetCachedAsync(cacheKey, results);

            return TypedResults.Ok(results);
        }
        catch
        {
            results = Array.Empty<TorrentInfo>();
            return TypedResults.Ok(results);
        }
        finally
        {
            sw.Stop();
            try
            {
                await auditService.LogQueryAsync(
                    query: queryRequest.QueryText ?? string.Empty,
                    endpoint: "/dmm/search",
                    clientIp: context.Connection.RemoteIpAddress?.ToString(),
                    resultCount: results.Length,
                    durationMs: (int)sw.ElapsedMilliseconds,
                    timestamp: timestamp);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to log query audit for unfiltered search");
            }
        }
    }

    private static async Task<Ok<TorrentInfo[]>> PerformFilteredSearch(HttpContext context, ITorrentInfoService torrentInfoService, ZileanConfiguration configuration, ILogger<DmmFilteredInstance> logger, IQueryAuditService auditService, ZileanDbContext dbContext, IQueryCacheService queryCache, [AsParameters] SearchFilteredRequest request)
    {

        var sw = Stopwatch.StartNew();
        var timestamp = DateTime.UtcNow;
        TorrentInfo[] results = Array.Empty<TorrentInfo>();

        try
        {
            var cacheKey = $"filtered:{request.Query}:{request.Season}:{request.Episode}:{request.Year}:{request.Language}:{request.Resolution}:{request.Category}:{request.ImdbId}";
            var cached = await queryCache.GetCachedAsync(cacheKey);
            if (cached != null)
            {
                logger.LogInformation("Cache hit for filtered search query: {Query}", request.Query);
                return TypedResults.Ok(cached);
            }

            logger.LogInformation("Performing filtered search for {@Request}", request);

            results = await torrentInfoService.SearchForTorrentInfoFiltered(new TorrentInfoFilter
            {
                Query = request.Query,
                Season = request.Season,
                Episode = request.Episode,
                Year = request.Year,
                Language = request.Language,
                Resolution = request.Resolution,
                Category = request.Category,
                ImdbId = request.ImdbId
            });

            logger.LogInformation("Filtered search for {QueryText} returned {Count} results", request.Query, results.Length);

            if (results.Length == 0)
            {
                try
                {
                    await TrackSearchMissAsync(dbContext, request.Query, request.Category, logger);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to track search miss for query {QueryText}", request.Query);
                }
            }

            await queryCache.SetCachedAsync(cacheKey, results);

            return TypedResults.Ok(results);
        }
        catch
        {
            results = Array.Empty<TorrentInfo>();
            return TypedResults.Ok(results);
        }
        finally
        {
            sw.Stop();
            try
            {
                var filtersJson = JsonSerializer.Serialize(request);
                await auditService.LogQueryAsync(
                    query: request.Query ?? string.Empty,
                    endpoint: "/dmm/filtered",
                    clientIp: context.Connection.RemoteIpAddress?.ToString(),
                    resultCount: results.Length,
                    durationMs: (int)sw.ElapsedMilliseconds,
                    timestamp: timestamp,
                    filtersJson: filtersJson);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to log query audit for filtered search");
            }
        }
    }

    /// <summary>
    /// Tracks a search miss by finding similar torrents via trigram similarity,
    /// incrementing their miss count, and marking them as refresh pending.
    /// </summary>
    private static async Task TrackSearchMissAsync(ZileanDbContext dbContext, string? query, string? category, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var cleanQuery = Parsing.CleanQuery(query);

        logger.LogInformation("Tracking search miss for query: {Query}, category: {Category}", cleanQuery, category);

        const string sql = """
            UPDATE "Torrents"
            SET "MissCount" = "MissCount" + 1,
                "RefreshPending" = true
            WHERE "CleanedParsedTitle" % @query
              AND (@category IS NULL OR "Category" = @category)
            """;

        var parameters = new[]
        {
            new NpgsqlParameter("@query", cleanQuery),
            new NpgsqlParameter("@category", string.IsNullOrEmpty(category) ? DBNull.Value : category),
        };

        var affectedRows = await dbContext.Database.ExecuteSqlRawAsync(sql, parameters);

        logger.LogInformation("Tracked search miss for {Query}: marked {AffectedRows} similar torrents as refresh pending", cleanQuery, affectedRows);
    }

    private abstract class DmmUnfilteredInstance;
    private abstract class DmmFilteredInstance;
    private abstract class GeneralInstance;
}
