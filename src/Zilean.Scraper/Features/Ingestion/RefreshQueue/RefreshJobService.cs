namespace Zilean.Scraper.Features.Ingestion.RefreshQueue;

/// <summary>
/// Service for managing the refresh job queue.
/// Handles query-triggered background hydration with deduplication.
/// </summary>
public class RefreshJobService(
    ZileanDbContext dbContext,
    ILogger<RefreshJobService> logger,
    IOptions<AggressivePersistenceOptions> options)
{
    private readonly AggressivePersistenceOptions _options = options.Value;

    /// <summary>
    /// Create a new refresh job triggered by a query miss.
    /// Returns false if a similar job already exists (deduplication).
    /// </summary>
    public async Task<bool> CreateRefreshJobAsync(
        string normalizedQuery,
        string? queryFingerprint,
        RefreshJobTrigger triggerType,
        CancellationToken cancellationToken)
    {
        // Deduplication: check for existing pending/processing jobs within dedupe window
        var dedupeWindowStart = DateTime.UtcNow.AddMinutes(-_options.RefreshDedupeWindowMinutes);
        
        var existingJob = await dbContext.RefreshJobs
            .AnyAsync(j => 
                j.QueryFingerprint == queryFingerprint &&
                j.NormalizedQuery == normalizedQuery &&
                j.Status == RefreshJobStatus.Pending &&
                j.CreatedAt >= dedupeWindowStart, 
                cancellationToken);
        
        if (existingJob)
        {
            logger.LogDebug("Refresh job deduplicated: {Query}", normalizedQuery);
            return false;
        }

        var job = new RefreshJob
        {
            TriggerType = triggerType,
            QueryFingerprint = queryFingerprint,
            NormalizedQuery = normalizedQuery,
            Status = RefreshJobStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ScheduledAt = DateTime.UtcNow,
            RetryCount = 0
        };
        
        await dbContext.RefreshJobs.AddAsync(job, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        
        logger.LogInformation("Refresh job created: {Query}, trigger={Trigger}", normalizedQuery, triggerType);
        return true;
    }

    /// <summary>
    /// Get pending refresh jobs to process (bounded by batch size).
    /// </summary>
    public async Task<List<RefreshJob>> GetPendingJobsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.RefreshJobs
            .Where(j => j.Status == RefreshJobStatus.Pending && j.ScheduledAt <= DateTime.UtcNow)
            .OrderBy(j => j.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Mark a job as being processed.
    /// </summary>
    public async Task MarkJobProcessingAsync(RefreshJob job, CancellationToken cancellationToken)
    {
        job.Status = RefreshJobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Mark a job as completed successfully.
    /// </summary>
    public async Task MarkJobCompletedAsync(RefreshJob job, int entriesAdded, CancellationToken cancellationToken)
    {
        job.Status = RefreshJobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        job.EntriesAdded = entriesAdded;
        await dbContext.SaveChangesAsync(cancellationToken);
        
        logger.LogInformation("Refresh job completed: {Query}, entries={Entries}", job.NormalizedQuery, entriesAdded);
    }

    /// <summary>
    /// Mark a job as failed with retry logic.
    /// </summary>
    public async Task MarkJobFailedAsync(RefreshJob job, string error, CancellationToken cancellationToken)
    {
        job.Status = RefreshJobStatus.Failed;
        job.CompletedAt = DateTime.UtcNow;
        job.ErrorSummary = TruncateError(error, 500);
        job.RetryCount++;
        
        // Schedule retry with exponential backoff if under max retries
        if (job.RetryCount < _options.MaxRetriesPerJob)
        {
            var backoffMinutes = Math.Min(5 * Math.Pow(2, job.RetryCount), 120);
            job.ScheduledAt = DateTime.UtcNow.AddMinutes(backoffMinutes);
            job.Status = RefreshJobStatus.Pending; // Reset to pending for retry
            logger.LogWarning("Refresh job failed, scheduled for retry: {Query}, retry={Retry}, error={Error}", 
                job.NormalizedQuery, job.RetryCount, job.ErrorSummary);
        }
        else
        {
            logger.LogError("Refresh job failed permanently: {Query}, error={Error}", job.NormalizedQuery, job.ErrorSummary);
        }
        
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Get refresh job statistics.
    /// </summary>
    public async Task<RefreshJobStats> GetJobStatsAsync(CancellationToken cancellationToken)
    {
        var total = await dbContext.RefreshJobs.CountAsync(cancellationToken);
        var pending = await dbContext.RefreshJobs.CountAsync(j => j.Status == RefreshJobStatus.Pending, cancellationToken);
        var processing = await dbContext.RefreshJobs.CountAsync(j => j.Status == RefreshJobStatus.Processing, cancellationToken);
        var completed = await dbContext.RefreshJobs.CountAsync(j => j.Status == RefreshJobStatus.Completed, cancellationToken);
        var failed = await dbContext.RefreshJobs.CountAsync(j => j.Status == RefreshJobStatus.Failed, cancellationToken);
        var todayCreated = await dbContext.RefreshJobs.CountAsync(j => j.CreatedAt.Date == DateTime.UtcNow.Date, cancellationToken);
        var todayCompleted = await dbContext.RefreshJobs.CountAsync(j => j.CompletedAt.HasValue && j.CompletedAt.Value.Date == DateTime.UtcNow.Date, cancellationToken);
        
        return new RefreshJobStats
        {
            Total = total,
            Pending = pending,
            Processing = processing,
            Completed = completed,
            Failed = failed,
            TodayCreated = todayCreated,
            TodayCompleted = todayCompleted
        };
    }

    /// <summary>
    /// Clean up old completed jobs (retention policy).
    /// </summary>
    public async Task<int> CleanupOldJobsAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var oldJobs = await dbContext.RefreshJobs
            .Where(j => j.Status == RefreshJobStatus.Completed && j.CompletedAt < cutoff)
            .ToListAsync(cancellationToken);
        
        if (oldJobs.Count > 0)
        {
            dbContext.RefreshJobs.RemoveRange(oldJobs);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Cleaned up {Count} old refresh jobs", oldJobs.Count);
        }
        
        return oldJobs.Count;
    }

    private static string TruncateError(string error, int maxLength)
    {
        if (string.IsNullOrEmpty(error)) return error;
        return error.Length <= maxLength ? error : error[..maxLength];
    }
}

/// <summary>
/// Statistics about refresh jobs.
/// </summary>
public class RefreshJobStats
{
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Processing { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public int TodayCreated { get; init; }
    public int TodayCompleted { get; init; }
}
