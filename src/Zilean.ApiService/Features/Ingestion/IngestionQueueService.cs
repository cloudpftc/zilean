using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zilean.Shared.Features.Ingestion;

namespace Zilean.ApiService.Features.Ingestion;

public class IngestionQueueService : IIngestionQueueService
{
    private readonly ZileanDbContext _dbContext;
    private readonly ILogger<IngestionQueueService> _logger;

    public IngestionQueueService(ZileanDbContext dbContext, ILogger<IngestionQueueService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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
        var entry = await _dbContext.IngestionQueues
            .Where(q => q.Status == "pending")
            .OrderBy(q => q.CreatedAt)
            .FirstOrDefaultAsync();

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

        entry.Status = string.IsNullOrEmpty(errorMessage) ? "completed" : "failed";
        entry.ProcessedAt = DateTime.UtcNow;
        entry.ErrorMessage = errorMessage;

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
