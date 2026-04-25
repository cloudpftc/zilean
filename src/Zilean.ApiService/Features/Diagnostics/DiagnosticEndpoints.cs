using Zilean.ApiService.Features.Ingestion;
using Zilean.Database;

namespace Zilean.ApiService.Features.Diagnostics;

public static class DiagnosticEndpoints
{
    private const string GroupName = "diagnostics";

    public static WebApplication MapDiagnosticEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(GroupName)
            .WithTags(GroupName)
            .RequireAuthorization();

        group.MapGet("/freshness", GetFreshness);
        group.MapGet("/queue", GetQueue);
        group.MapGet("/misses", GetMisses);
        group.MapGet("/stats", GetStats);

        return app;
    }

    private static async Task<IResult> GetFreshness(ZileanDbContext dbContext, CancellationToken ct)
    {
        var sourceStats = await dbContext.Torrents
            .GroupBy(t => t.Category)
            .Select(g => new
            {
                name = g.Key,
                lastUpdated = g.Max(t => t.LastRefreshedAt != null ? t.LastRefreshedAt : t.IngestedAt),
                torrentCount = g.Count()
            })
            .OrderByDescending(x => x.lastUpdated)
            .ToListAsync(ct);

        var sources = sourceStats.Select(s => new
        {
            name = s.name,
            lastUpdated = s.lastUpdated,
            ageHours = s.lastUpdated != null ? Math.Round((DateTime.UtcNow - s.lastUpdated.Value).TotalHours, 2) : (double?)null,
            torrentCount = s.torrentCount
        }).ToList();

        var overallLastUpdated = sourceStats.Max(s => s.lastUpdated);
        var overallAgeHours = overallLastUpdated != null
            ? Math.Round((DateTime.UtcNow - overallLastUpdated.Value).TotalHours, 2)
            : (double?)null;

        return TypedResults.Ok(new
        {
            sources,
            overallAgeHours,
            overallLastUpdated,
            totalTorrents = sources.Sum(s => s.torrentCount)
        });
    }

    private static async Task<IResult> GetQueue(IIngestionQueueService queueService, CancellationToken ct)
    {
        var stats = await queueService.GetStatsAsync();
        var pendingItems = await queueService.GetPendingAsync(10);

        var oldestPending = pendingItems.Select(q => new
        {
            q.Id,
            q.InfoHash,
            q.CreatedAt,
            q.RetryCount
        }).ToList();

        return TypedResults.Ok(new
        {
            stats.Pending,
            stats.Processing,
            stats.Completed,
            stats.Failed,
            oldestPending
        });
    }

    private static IResult GetMisses() => Results.Ok(new
    {
        status = "not_implemented",
        message = "Miss tracking coming soon"
    });

    private static IResult GetStats() => Results.Ok(new
    {
        status = "not_implemented",
        message = "Stats endpoint coming soon"
    });
}
