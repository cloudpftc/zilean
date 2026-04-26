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
