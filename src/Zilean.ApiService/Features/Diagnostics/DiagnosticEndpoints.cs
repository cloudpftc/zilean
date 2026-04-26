using Dapper;
using Npgsql;
using Zilean.ApiService.Features.Ingestion;
using Zilean.ApiService.Features.Search;
using Zilean.Database;
using Zilean.Shared.Features.Configuration;

namespace Zilean.ApiService.Features.Diagnostics;

public static class DiagnosticEndpoints
{
    private const string GroupName = "diagnostics";

    public static WebApplication MapDiagnosticEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(GroupName)
            .WithTags(GroupName);

        group.MapGet("/freshness", GetFreshness);
        group.MapGet("/queue", GetQueue);
        group.MapGet("/misses", GetMisses);
        group.MapGet("/stats", GetStats);
        group.MapGet("/cache", GetCacheStats);

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

    private static async Task<IResult> GetMisses(ZileanDbContext dbContext, CancellationToken ct)
    {
        var totalMisses = await dbContext.Torrents
            .Where(t => t.RefreshPending || t.MissCount > 0)
            .SumAsync(t => t.MissCount, ct);

        var topMissed = await dbContext.Torrents
            .Where(t => t.RefreshPending || t.MissCount > 0)
            .OrderByDescending(t => t.MissCount)
            .Take(20)
            .Select(t => new
            {
                title = t.RawTitle ?? t.ParsedTitle ?? "Unknown",
                missCount = t.MissCount,
                imdbId = t.ImdbId,
                category = t.Category
            })
            .ToListAsync(ct);

        return TypedResults.Ok(new
        {
            totalMisses,
            topMissed
        });
    }

    private static async Task<IResult> GetStats(ZileanDbContext dbContext, ZileanConfiguration configuration, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(configuration.Database.ConnectionString);
        await connection.OpenAsync(ct);

        var tableStats = await connection.QueryAsync<TableStatRaw>("""
            SELECT
                s.relname AS "Name",
                s.n_live_tup AS "RowCount",
                pg_total_relation_size(c.oid) AS "SizeBytes"
            FROM pg_stat_user_tables s
            JOIN pg_class c ON c.relname = s.relname
            ORDER BY pg_total_relation_size(c.oid) DESC
            """);

        var tables = tableStats.Select(t => new
        {
            name = t.Name,
            rowCount = t.RowCount,
            sizeBytes = t.SizeBytes,
            sizeMb = Math.Round(t.SizeBytes / (1024.0 * 1024.0), 2)
        }).ToList();

        var totalDatabaseSizeBytes = await connection.QuerySingleOrDefaultAsync<long>("SELECT pg_database_size(current_database())");

        var lastIngestionTime = await dbContext.IngestionQueues
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => q.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return TypedResults.Ok(new
        {
            tables,
            totalDatabaseSizeBytes,
            totalDatabaseSizeMb = Math.Round(totalDatabaseSizeBytes / (1024.0 * 1024.0), 2),
            lastIngestionTime
        });
    }

    private static Task<Ok<CacheStats>> GetCacheStats(IQueryCacheService queryCache)
    {
        return Task.FromResult(TypedResults.Ok(queryCache.GetStats()));
    }

    private class TableStatRaw
    {
        public string Name { get; set; } = string.Empty;
        public long RowCount { get; set; }
        public long SizeBytes { get; set; }
    }
}
