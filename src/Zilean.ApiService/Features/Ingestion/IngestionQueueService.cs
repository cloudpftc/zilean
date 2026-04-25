using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zilean.Shared.Features.Configuration;
using Zilean.Shared.Features.Ingestion;

namespace Zilean.ApiService.Features.Ingestion;

public class IngestionQueueService : IIngestionQueueService
{
    private readonly ZileanDbContext _dbContext;
    private readonly ILogger<IngestionQueueService> _logger;
    private readonly ZileanConfiguration _configuration;

    public IngestionQueueService(ZileanDbContext dbContext, ILogger<IngestionQueueService> logger, ZileanConfiguration configuration)
    {
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<IngestionQueue> EnqueueAsync(string infoHash)
    {
        var entry = new IngestionQueue
        {
            InfoHash = infoHash,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };

        _dbContext.IngestionQueues.Add(entry);
        await _dbContext.SaveChangesAsync();

        _logger.LogDebug("Enqueued ingestion item: {InfoHash} (id={Id})", infoHash, entry.Id);
        return entry;
    }

    public async Task<IngestionQueue?> DequeueAsync()
    {
        var maxRetryCount = _configuration.Persistence.MaxRetryCount;

        // First, try to get a pending item
        var entry = await _dbContext.IngestionQueues
            .Where(q => q.Status == "pending")
            .OrderBy(q => q.CreatedAt)
            .FirstOrDefaultAsync();

        // If no pending items, check for failed items eligible for retry
        if (entry == null)
        {
            var now = DateTime.UtcNow;
            var retryableItems = await _dbContext.IngestionQueues
                .Where(q => q.Status == "failed" && q.RetryCount < maxRetryCount)
                .ToListAsync();

            entry = retryableItems
                .Where(q => q.ProcessedAt.HasValue && q.ProcessedAt.Value < now.AddMinutes(-Math.Pow(2, q.RetryCount) * 5))
                .OrderBy(q => q.ProcessedAt)
                .FirstOrDefault();
        }

        if (entry == null)
        {
            return null;
        }

        entry.Status = "processing";
        await _dbContext.SaveChangesAsync();

        _logger.LogDebug("Dequeued ingestion item: {InfoHash} (id={Id})", entry.InfoHash, entry.Id);
        return entry;
    }

    public async Task MarkProcessedAsync(int id, string? errorMessage = null)
    {
        var entry = await _dbContext.IngestionQueues.FindAsync(id);
        if (entry == null)
        {
            _logger.LogWarning("Attempted to mark non-existent queue item as processed: {Id}", id);
            return;
        }

        entry.ProcessedAt = DateTime.UtcNow;
        entry.ErrorMessage = errorMessage;

        if (string.IsNullOrEmpty(errorMessage))
        {
            entry.Status = "completed";
        }
        else if (entry.RetryCount < _configuration.Persistence.MaxRetryCount)
        {
            entry.RetryCount++;
            entry.Status = "pending";
        }
        else
        {
            entry.Status = "failed";
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogDebug("Marked ingestion item as {Status}: {InfoHash} (id={Id})", entry.Status, entry.InfoHash, id);
    }

    public async Task<IEnumerable<IngestionQueue>> GetPendingAsync(int limit = 50)
    {
        return await _dbContext.IngestionQueues
            .Where(q => q.Status == "pending")
            .OrderBy(q => q.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<IngestionQueue>> GetRetryableFailedAsync(int limit = 50)
    {
        var maxRetryCount = _configuration.Persistence.MaxRetryCount;
        var now = DateTime.UtcNow;

        var retryableItems = await _dbContext.IngestionQueues
            .Where(q => q.Status == "failed" && q.RetryCount < maxRetryCount)
            .ToListAsync();

        return retryableItems
            .Where(q => q.ProcessedAt.HasValue && q.ProcessedAt.Value < now.AddMinutes(-Math.Pow(2, q.RetryCount) * 5))
            .OrderBy(q => q.ProcessedAt)
            .Take(limit)
            .ToList();
    }

    public async Task<QueueStats> GetStatsAsync()
    {
        var stats = await _dbContext.IngestionQueues
            .GroupBy(q => q.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        return new QueueStats(
            Pending: stats.GetValueOrDefault("pending", 0),
            Processing: stats.GetValueOrDefault("processing", 0),
            Completed: stats.GetValueOrDefault("completed", 0),
            Failed: stats.GetValueOrDefault("failed", 0)
        );
    }
}
