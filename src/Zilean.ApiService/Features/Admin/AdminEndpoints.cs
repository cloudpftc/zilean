using Zilean.ApiService.Features.Authentication;
using Zilean.ApiService.Features.Sync;
using Zilean.Database;
using Zilean.Shared.Features.Configuration;

namespace Zilean.ApiService.Features.Admin;

public static class AdminEndpoints
{
    private const string GroupName = "admin";

    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(GroupName)
            .WithTags(GroupName)
            .DisableAntiforgery()
            .RequireAuthorization(ApiKeyAuthentication.Policy)
            .WithMetadata(new OpenApiSecurityMetadata(ApiKeyAuthentication.Scheme));

        group.MapGet("/sources/status", GetSourcesStatus);
        group.MapPost("/sources/trigger/{sourceName}", TriggerSourceSync);
        group.MapPost("/sources/backfill/{sourceName}", BackfillSource);
        group.MapPost("/sources/backfill-all", BackfillAllSources);
        group.MapPost("/sources/backfill-imdb", BackfillImdb);

        return app;
    }

    private static async Task<IResult> GetSourcesStatus(ZileanConfiguration configuration,
        ZileanDbContext dbContext)
    {
        var stats = await dbContext.TorrentSourceStats
            .ToListAsync();

        var results = configuration.Prowlarr.Indexers
            .Select(indexer =>
            {
                var matchingStat = stats.FirstOrDefault(s =>
                    s.Source.Equals(indexer.SourceName, StringComparison.OrdinalIgnoreCase));

                return new SourceStatusResponse
                {
                    SourceName = indexer.SourceName,
                    Enabled = indexer.Enabled,
                    IndexerId = indexer.IndexerId,
                    Categories = indexer.Categories,
                    Cron = configuration.Prowlarr.Cron,
                    LastSyncAt = matchingStat?.LastSyncAt,
                    TorrentCount = matchingStat?.TorrentCount ?? 0,
                    LastError = matchingStat?.LastError,
                };
            })
            .ToList();

        return TypedResults.Ok(results);
    }

    private static async Task<IResult> TriggerSourceSync(string sourceName,
        ProwlarrSyncJob syncJob,
        ILogger<ProwlarrSyncJob> logger)
    {
        try
        {
            var count = await syncJob.SyncSingleIndexerAsync(sourceName);
            logger.LogInformation("Manual sync triggered for '{SourceName}': {Count} torrents processed",
                sourceName, count);

            return TypedResults.Ok(new TriggerResponse
            {
                SourceName = sourceName,
                TorrentsProcessed = count,
                Message = $"Sync completed for '{sourceName}'",
            });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Indexer Not Found");
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Sync Failed");
        }
    }

    private static IResult BackfillSource(
        string sourceName,
        [FromQuery] string untilDate,
        IServiceScopeFactory scopeFactory)
    {
        if (string.IsNullOrWhiteSpace(untilDate))
        {
            return TypedResults.BadRequest("untilDate is required. Use YYYY-MM-DD format.");
        }

        if (!DateTime.TryParse(untilDate, out var date))
        {
            return TypedResults.BadRequest("Invalid date format. Use YYYY-MM-DD.");
        }

        var utcDate = date.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : date.ToUniversalTime();

        // Fire-and-forget: start backfill in background and return immediately
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var syncJob = scope.ServiceProvider.GetRequiredService<ProwlarrSyncJob>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ProwlarrSyncJob>>();

            try
            {
                await syncJob.BackfillIndexerAsync(sourceName, utcDate);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ProwlarrBackfill] Background backfill failed for '{SourceName}'", sourceName);
            }
        });

        return TypedResults.Ok(new
        {
            sourceName,
            untilDate,
            message = "Backfill started in background",
            status = "running",
        });
    }

    private static IResult BackfillAllSources(
        [FromQuery] string untilDate,
        ZileanConfiguration configuration,
        IServiceScopeFactory scopeFactory)
    {
        if (string.IsNullOrWhiteSpace(untilDate))
        {
            return TypedResults.BadRequest("untilDate is required. Use YYYY-MM-DD format.");
        }

        if (!DateTime.TryParse(untilDate, out var date))
        {
            return TypedResults.BadRequest("Invalid date format. Use YYYY-MM-DD.");
        }

        var utcDate = date.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : date.ToUniversalTime();

        var enabledIndexers = configuration.Prowlarr.Indexers
            .Where(i => i.Enabled && !string.IsNullOrWhiteSpace(i.SourceName))
            .ToList();

        if (enabledIndexers.Count == 0)
        {
            return TypedResults.BadRequest("No enabled Prowlarr indexers found.");
        }

        // Fire-and-forget: start backfill for all sources in background and return immediately
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var syncJob = scope.ServiceProvider.GetRequiredService<ProwlarrSyncJob>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ProwlarrSyncJob>>();

            foreach (var indexer in enabledIndexers)
            {
                try
                {
                    logger.LogInformation("[ProwlarrBackfill] Starting background backfill for '{SourceName}'", indexer.SourceName);
                    await syncJob.BackfillIndexerAsync(indexer.SourceName, utcDate);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[ProwlarrBackfill] Background backfill failed for '{SourceName}'", indexer.SourceName);
                }
            }
        });

        return TypedResults.Ok(new
        {
            sources = enabledIndexers.Select(i => i.SourceName).ToList(),
            untilDate,
            message = "Backfill started for all sources in background",
            status = "running",
        });
    }

    private static IResult BackfillImdb(IServiceScopeFactory scopeFactory)
    {
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var syncJob = scope.ServiceProvider.GetRequiredService<ProwlarrSyncJob>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ProwlarrSyncJob>>();

            try
            {
                var count = await syncJob.BackfillFromImdbTitlesAsync();
                logger.LogInformation("[ImdbBackfill] Completed: {Count} torrents ingested", count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ImdbBackfill] Background IMDb backfill failed");
            }
        });

        return TypedResults.Ok(new
        {
            message = "IMDb-driven backfill started in background",
            status = "running",
        });
    }
}

public class SourceStatusResponse
{
    public string SourceName { get; set; } = default!;
    public bool Enabled { get; set; }
    public int IndexerId { get; set; }
    public string Categories { get; set; } = default!;
    public string? Cron { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public long TorrentCount { get; set; }
    public string? LastError { get; set; }
}

public class TriggerResponse
{
    public string SourceName { get; set; } = default!;
    public int TorrentsProcessed { get; set; }
    public string Message { get; set; } = default!;
}
