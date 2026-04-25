using Zilean.ApiService.Features.Ingestion;

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

    private static IResult GetFreshness() => Results.Ok(new
    {
        status = "not_implemented",
        message = "Freshness tracking coming soon"
    });

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
