namespace Zilean.ApiService.Features.Diagnostics;

/// <summary>
/// Diagnostic endpoints for operational visibility.
/// Provides health checks, freshness info, queue status, and search diagnostics.
/// </summary>
public static class DiagnosticEndpoints
{
    private const string BasePath = "/diagnostics";

    public static WebApplication MapDiagnosticEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(BasePath)
            .WithTags("Diagnostics")
            .RequireAuthorization(ApiKeyAuthentication.Policy)
            .WithMetadata(new OpenApiSecurityMetadata(ApiKeyAuthentication.Scheme));

        group.MapGet("/freshness", GetFreshnessDiagnostics)
            .Produces<FreshnessDiagnosticsResponse>()
            .WithName("GetFreshnessDiagnostics");

        group.MapGet("/ingestion", GetIngestionDiagnostics)
            .Produces<IngestionDiagnosticsResponse>()
            .WithName("GetIngestionDiagnostics");

        group.MapGet("/queue", GetQueueDiagnostics)
            .Produces<QueueDiagnosticsResponse>()
            .WithName("GetQueueDiagnostics");

        group.MapGet("/misses", GetMissDiagnostics)
            .Produces<MissDiagnosticsResponse>()
            .WithName("GetMissDiagnostics");

        group.MapGet("/anime", GetAnimeDiagnostics)
            .Produces<AnimeDiagnosticsResponse>()
            .WithName("GetAnimeDiagnostics");

        group.MapGet("/search", GetSearchDiagnostics)
            .Produces<SearchDiagnosticsResponse>()
            .WithName("GetSearchDiagnostics");

        return app;
    }

    private static async Task<IResult> GetFreshnessDiagnostics(
        ZileanDbContext dbContext,
        IOptions<AggressivePersistenceOptions> options)
    {
        try
        {
            var now = DateTime.UtcNow;
            
            var segmentStats = await dbContext.SourceSegments
                .GroupBy(s => s.SourceType)
                .Select(g => new SourceTypeFreshness
                {
                    SourceType = g.Key.ToString(),
                    Total = g.Count(),
                    Stale = g.Count(s => s.StaleAfter <= now),
                    Pending = g.Count(s => s.Status == SegmentStatus.Pending),
                    LastSuccessful = g.Max(s => s.LastSuccessfulAt)
                })
                .ToListAsync();

            var oldestStaleSegment = await dbContext.SourceSegments
                .Where(s => s.StaleAfter <= now)
                .OrderBy(s => s.StaleAfter)
                .Select(s => new { s.SegmentId, s.SourceType, s.StaleAfter })
                .FirstOrDefaultAsync();

            var response = new FreshnessDiagnosticsResponse
            {
                Timestamp = now,
                OverallHealth = segmentStats.All(s => s.Stale == 0) ? "Healthy" : "Degraded",
                Sources = segmentStats,
                OldestStaleSegment = oldestStaleSegment,
                StaleSegmentTtlMinutes = options.Value.StaleSegmentTtlMinutes
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    private static async Task<IResult> GetIngestionDiagnostics(
        ZileanDbContext dbContext,
        SegmentService segmentService)
    {
        try
        {
            var segmentStats = await segmentService.GetSegmentStatsAsync(CancellationToken.None);
            
            var recentSyncRuns = await dbContext.SourceSyncRuns
                .OrderByDescending(r => r.StartTime)
                .Take(10)
                .Select(r => new RecentSyncRun
                {
                    Id = r.Id,
                    SourceType = r.SourceType.ToString(),
                    Status = r.Status.ToString(),
                    StartTime = r.StartTime,
                    EndTime = r.EndTime,
                    PagesProcessed = r.PagesProcessed,
                    EntriesAdded = r.EntriesAdded
                })
                .ToListAsync();

            var checkpoints = await dbContext.IngestionCheckpoints
                .Select(c => new CheckpointInfo
                {
                    SourceType = c.SourceType.ToString(),
                    Key = c.CheckpointKey,
                    Value = c.CheckpointValue,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync();

            var response = new IngestionDiagnosticsResponse
            {
                SegmentStats = segmentStats,
                RecentSyncRuns = recentSyncRuns,
                Checkpoints = checkpoints
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    private static async Task<IResult> GetQueueDiagnostics(
        RefreshJobService refreshJobService,
        ZileanDbContext dbContext)
    {
        try
        {
            var jobStats = await refreshJobService.GetJobStatsAsync(CancellationToken.None);
            
            var pendingJobs = await dbContext.RefreshJobs
                .Where(j => j.Status == RefreshJobStatus.Pending)
                .OrderBy(j => j.CreatedAt)
                .Take(20)
                .Select(j => new QueuedJobInfo
                {
                    Id = j.Id,
                    NormalizedQuery = j.NormalizedQuery,
                    TriggerType = j.TriggerType.ToString(),
                    CreatedAt = j.CreatedAt,
                    ScheduledAt = j.ScheduledAt,
                    RetryCount = j.RetryCount
                })
                .ToListAsync();

            var processingJobs = await dbContext.RefreshJobs
                .Where(j => j.Status == RefreshJobStatus.Processing)
                .Select(j => new ProcessingJobInfo
                {
                    Id = j.Id,
                    NormalizedQuery = j.NormalizedQuery,
                    StartedAt = j.StartedAt!.Value
                })
                .ToListAsync();

            var response = new QueueDiagnosticsResponse
            {
                Stats = jobStats,
                PendingJobs = pendingJobs,
                ProcessingJobs = processingJobs
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    private static async Task<IResult> GetMissDiagnostics(
        ZileanDbContext dbContext,
        IOptions<AggressivePersistenceOptions> options)
    {
        try
        {
            var totalMisses = await dbContext.QueryMisses.CountAsync();
            
            var topMisses = await dbContext.QueryMisses
                .OrderByDescending(m => m.MissCount)
                .Take(50)
                .Select(m => new QueryMissInfo
                {
                    Fingerprint = m.NormalizedQueryFingerprint,
                    Examples = m.RawQueryExamples.Take(3).ToList(),
                    MissCount = m.MissCount,
                    LastSeen = m.LastSeen,
                    RefreshTriggered = m.RefreshTriggered,
                    ContentHints = m.ContentHints
                })
                .ToListAsync();

            var missesWithRefresh = await dbContext.QueryMisses.CountAsync(m => m.RefreshTriggered);
            var missesLast24h = await dbContext.QueryMisses.CountAsync(m => m.LastSeen > DateTime.UtcNow.AddHours(-24));

            var response = new MissDiagnosticsResponse
            {
                TotalMisses = totalMisses,
                MissesLast24Hours = missesLast24h,
                MissesWithRefreshScheduled = missesWithRefresh,
                TopMisses = topMisses,
                MaxTracked = options.Value.MaxQueryMissesToTrack
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    private static async Task<IResult> GetAnimeDiagnostics(
        ZileanDbContext dbContext,
        IOptions<AggressivePersistenceOptions> options)
    {
        try
        {
            var animeEnabled = options.Value.AnimeNormalizationEnabled;
            
            var animeDocuments = await dbContext.SearchDocuments
                .Where(d => d.ContentType == "anime")
                .CountAsync();

            var totalDocuments = await dbContext.SearchDocuments.CountAsync();
            
            var animeAliases = await dbContext.TitleAliases
                .Where(a => a.AliasType == "anime")
                .CountAsync();

            var response = new AnimeDiagnosticsResponse
            {
                AnimeNormalizationEnabled = animeEnabled,
                AnimeDocumentCount = animeDocuments,
                TotalDocumentCount = totalDocuments,
                AnimeAliasCount = animeAliases,
                AnimePercentage = totalDocuments > 0 
                    ? Math.Round(animeDocuments * 100.0 / totalDocuments, 2) 
                    : 0
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    private static async Task<IResult> GetSearchDiagnostics(
        ZileanDbContext dbContext,
        IOptions<AggressivePersistenceOptions> options)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            
            var queriesToday = await dbContext.QueryAudits.CountAsync(q => q.CreatedAt >= today);
            var emptyResultsToday = await dbContext.QueryAudits.CountAsync(q => q.CreatedAt >= today && q.ResultsReturned == 0);
            var avgConfidenceToday = await dbContext.QueryAudits
                .Where(q => q.CreatedAt >= today && q.TopConfidence.HasValue)
                .AverageAsync(q => q.TopConfidence);

            var recentQueries = await dbContext.QueryAudits
                .OrderByDescending(q => q.CreatedAt)
                .Take(20)
                .Select(q => new RecentQueryAudit
                {
                    RawQuery = q.RawQuery,
                    NormalizedQuery = q.NormalizedQuery,
                    ContentType = q.ContentType,
                    ResultsReturned = q.ResultsReturned,
                    TopConfidence = q.TopConfidence,
                    TriggeredRefresh = q.TriggeredRefresh,
                    CreatedAt = q.CreatedAt
                })
                .ToListAsync();

            var response = new SearchDiagnosticsResponse
            {
                QueriesToday = queriesToday,
                EmptyResultsToday = emptyResultsToday,
                HitRateToday = queriesToday > 0 
                    ? Math.Round((queriesToday - emptyResultsToday) * 100.0 / queriesToday, 2) 
                    : 0,
                AverageConfidenceToday = Math.Round(avgConfidenceToday ?? 0, 3),
                RecentQueries = recentQueries,
                AuditEnabled = options.Value.SearchAuditEnabled
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }
}

// Response DTOs

public class FreshnessDiagnosticsResponse
{
    public DateTime Timestamp { get; init; }
    public string OverallHealth { get; init; } = "Unknown";
    public List<SourceTypeFreshness> Sources { get; init; } = new();
    public object? OldestStaleSegment { get; init; }
    public int StaleSegmentTtlMinutes { get; init; }
}

public class SourceTypeFreshness
{
    public string SourceType { get; init; } = default!;
    public int Total { get; init; }
    public int Stale { get; init; }
    public int Pending { get; init; }
    public DateTime? LastSuccessful { get; init; }
}

public class IngestionDiagnosticsResponse
{
    public SegmentStats SegmentStats { get; init; } = default!;
    public List<RecentSyncRun> RecentSyncRuns { get; init; } = new();
    public List<CheckpointInfo> Checkpoints { get; init; } = new();
}

public class RecentSyncRun
{
    public Guid Id { get; init; }
    public string SourceType { get; init; } = default!;
    public string Status { get; init; } = default!;
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public int PagesProcessed { get; init; }
    public int EntriesAdded { get; init; }
}

public class CheckpointInfo
{
    public string SourceType { get; init; } = default!;
    public string Key { get; init; } = default!;
    public string Value { get; init; } = default!;
    public DateTime UpdatedAt { get; init; }
}

public class QueueDiagnosticsResponse
{
    public RefreshJobStats Stats { get; init; } = default!;
    public List<QueuedJobInfo> PendingJobs { get; init; } = new();
    public List<ProcessingJobInfo> ProcessingJobs { get; init; } = new();
}

public class QueuedJobInfo
{
    public Guid Id { get; init; }
    public string NormalizedQuery { get; init; } = default!;
    public string TriggerType { get; init; } = default!;
    public DateTime CreatedAt { get; init; }
    public DateTime ScheduledAt { get; init; }
    public int RetryCount { get; init; }
}

public class ProcessingJobInfo
{
    public Guid Id { get; init; }
    public string NormalizedQuery { get; init; } = default!;
    public DateTime StartedAt { get; init; }
}

public class MissDiagnosticsResponse
{
    public int TotalMisses { get; init; }
    public int MissesLast24Hours { get; init; }
    public int MissesWithRefreshScheduled { get; init; }
    public List<QueryMissInfo> TopMisses { get; init; } = new();
    public int MaxTracked { get; init; }
}

public class QueryMissInfo
{
    public string Fingerprint { get; init; } = default!;
    public List<string> Examples { get; init; } = new();
    public int MissCount { get; init; }
    public DateTime LastSeen { get; init; }
    public bool RefreshTriggered { get; init; }
    public string? ContentHints { get; init; }
}

public class AnimeDiagnosticsResponse
{
    public bool AnimeNormalizationEnabled { get; init; }
    public int AnimeDocumentCount { get; init; }
    public int TotalDocumentCount { get; init; }
    public int AnimeAliasCount { get; init; }
    public double AnimePercentage { get; init; }
}

public class SearchDiagnosticsResponse
{
    public int QueriesToday { get; init; }
    public int EmptyResultsToday { get; init; }
    public double HitRateToday { get; init; }
    public double AverageConfidenceToday { get; init; }
    public List<RecentQueryAudit> RecentQueries { get; init; } = new();
    public bool AuditEnabled { get; init; }
}

public class RecentQueryAudit
{
    public string RawQuery { get; init; } = default!;
    public string NormalizedQuery { get; init; } = default!;
    public string ContentType { get; init; } = default!;
    public int ResultsReturned { get; init; }
    public double? TopConfidence { get; init; }
    public bool TriggeredRefresh { get; init; }
    public DateTime CreatedAt { get; init; }
}
