namespace Zilean.Scraper.Features.Ingestion.Segments;

/// <summary>
/// Service for managing source segments with stale-segment tracking.
/// Enables incremental ingestion by tracking which segments/pages need refresh.
/// </summary>
public class SegmentService(
    ZileanDbContext dbContext,
    ILogger<SegmentService> logger,
    IOptions<AggressivePersistenceOptions> options)
{
    private readonly AggressivePersistenceOptions _options = options.Value;

    /// <summary>
    /// Get or create a segment for tracking.
    /// </summary>
    public async Task<SourceSegment> GetOrCreateSegmentAsync(
        SourceType sourceType, 
        string segmentId, 
        CancellationToken cancellationToken)
    {
        var segment = await dbContext.SourceSegments
            .FirstOrDefaultAsync(s => s.SourceType == sourceType && s.SegmentId == segmentId, cancellationToken);
        
        if (segment is null)
        {
            segment = new SourceSegment
            {
                SourceType = sourceType,
                SegmentId = segmentId,
                Status = SegmentStatus.Pending,
                LastAttemptedAt = null,
                LastSuccessfulAt = null,
                RetryCount = 0,
                StaleAfter = DateTime.UtcNow.AddMinutes(_options.StaleSegmentTtlMinutes),
                ErrorSummary = null
            };
            await dbContext.SourceSegments.AddAsync(segment, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogDebug("Created new segment: {SourceType}:{SegmentId}", sourceType, segmentId);
        }
        
        return segment;
    }

    /// <summary>
    /// Mark a segment as successfully processed.
    /// </summary>
    public async Task MarkSegmentSuccessAsync(SourceSegment segment, int entriesProcessed, CancellationToken cancellationToken)
    {
        segment.Status = SegmentStatus.Completed;
        segment.LastSuccessfulAt = DateTime.UtcNow;
        segment.LastAttemptedAt = DateTime.UtcNow;
        segment.RetryCount = 0;
        segment.EntriesProcessed = entriesProcessed;
        segment.StaleAfter = DateTime.UtcNow.AddMinutes(_options.StaleSegmentTtlMinutes);
        segment.ErrorSummary = null;
        
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Segment marked successful: {SourceType}:{SegmentId}, entries={Entries}", 
            segment.SourceType, segment.SegmentId, entriesProcessed);
    }

    /// <summary>
    /// Mark a segment as failed.
    /// </summary>
    public async Task MarkSegmentFailedAsync(SourceSegment segment, string error, CancellationToken cancellationToken)
    {
        segment.Status = SegmentStatus.Failed;
        segment.LastAttemptedAt = DateTime.UtcNow;
        segment.RetryCount++;
        segment.ErrorSummary = TruncateError(error, 500);
        
        // Exponential backoff: mark as stale after increasing intervals
        var backoffMinutes = Math.Min(_options.StaleSegmentTtlMinutes * Math.Pow(2, segment.RetryCount), 1440);
        segment.StaleAfter = DateTime.UtcNow.AddMinutes(backoffMinutes);
        
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Segment marked failed: {SourceType}:{SegmentId}, retries={Retries}, error={Error}", 
            segment.SourceType, segment.SegmentId, segment.RetryCount, segment.ErrorSummary);
    }

    /// <summary>
    /// Get segments that are due for refresh (stale).
    /// </summary>
    public async Task<List<SourceSegment>> GetStaleSegmentsAsync(SourceType? sourceType, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var query = dbContext.SourceSegments
            .Where(s => s.StaleAfter <= now && s.Status != SegmentStatus.Processing);
        
        if (sourceType.HasValue)
        {
            query = query.Where(s => s.SourceType == sourceType.Value);
        }
        
        return await query
            .OrderBy(s => s.StaleAfter)
            .Take(_options.BatchSize * 2) // Fetch more than batch size to allow filtering
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Mark a segment as being processed.
    /// </summary>
    public async Task MarkSegmentProcessingAsync(SourceSegment segment, CancellationToken cancellationToken)
    {
        segment.Status = SegmentStatus.Processing;
        segment.LastAttemptedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Invalidate a segment to force refresh.
    /// </summary>
    public async Task InvalidateSegmentAsync(SourceType sourceType, string segmentId, CancellationToken cancellationToken)
    {
        var segment = await dbContext.SourceSegments
            .FirstOrDefaultAsync(s => s.SourceType == sourceType && s.SegmentId == segmentId, cancellationToken);
        
        if (segment is not null)
        {
            segment.StaleAfter = DateTime.UtcNow;
            segment.Status = SegmentStatus.Pending;
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Segment invalidated: {SourceType}:{SegmentId}", sourceType, segmentId);
        }
    }

    /// <summary>
    /// Get segment statistics.
    /// </summary>
    public async Task<SegmentStats> GetSegmentStatsAsync(CancellationToken cancellationToken)
    {
        var total = await dbContext.SourceSegments.CountAsync(cancellationToken);
        var pending = await dbContext.SourceSegments.CountAsync(s => s.Status == SegmentStatus.Pending, cancellationToken);
        var completed = await dbContext.SourceSegments.CountAsync(s => s.Status == SegmentStatus.Completed, cancellationToken);
        var failed = await dbContext.SourceSegments.CountAsync(s => s.Status == SegmentStatus.Failed, cancellationToken);
        var processing = await dbContext.SourceSegments.CountAsync(s => s.Status == SegmentStatus.Processing, cancellationToken);
        var stale = await dbContext.SourceSegments.CountAsync(s => s.StaleAfter <= DateTime.UtcNow, cancellationToken);
        
        return new SegmentStats
        {
            Total = total,
            Pending = pending,
            Completed = completed,
            Failed = failed,
            Processing = processing,
            Stale = stale
        };
    }

    private static string TruncateError(string error, int maxLength)
    {
        if (string.IsNullOrEmpty(error)) return error;
        return error.Length <= maxLength ? error : error[..maxLength];
    }
}

/// <summary>
/// Statistics about source segments.
/// </summary>
public class SegmentStats
{
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public int Processing { get; init; }
    public int Stale { get; init; }
}
