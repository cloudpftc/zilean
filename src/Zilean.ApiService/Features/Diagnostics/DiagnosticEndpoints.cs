using Microsoft.EntityFrameworkCore;
using Zilean.Database;
using Zilean.Shared.Features.Ingestion;

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

    private static async Task<IResult> GetQueue(ZileanDbContext dbContext, CancellationToken ct)
    {
        var stats = await dbContext.IngestionQueues
            .GroupBy(q => q.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var pending = stats.FirstOrDefault(s => s.Status == "pending")?.Count ?? 0;
        var processing = stats.FirstOrDefault(s => s.Status == "processing")?.Count ?? 0;
        var completed = stats.FirstOrDefault(s => s.Status == "completed")?.Count ?? 0;
        var failed = stats.FirstOrDefault(s => s.Status == "failed")?.Count ?? 0;

        var oldestPending = await dbContext.IngestionQueues
            .Where(q => q.Status == "pending")
            .OrderBy(q => q.CreatedAt)
            .Take(10)
            .Select(q => new
            {
                q.Id,
                q.InfoHash,
                q.CreatedAt,
                q.RetryCount
            })
            .ToListAsync(ct);

        return TypedResults.Ok(new
        {
            pending,
            processing,
            completed,
            failed,
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
