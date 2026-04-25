using Zilean.Shared.Features.Ingestion;

namespace Zilean.ApiService.Features.Ingestion;

/// <summary>
/// Manages the ingestion queue for torrent processing.
/// </summary>
public interface IIngestionQueueService
{
    /// <summary>
    /// Enqueues a new item for ingestion processing.
    /// </summary>
    /// <param name="infoHash">The torrent info hash to enqueue.</param>
    /// <returns>The created ingestion queue entry.</returns>
    Task<IngestionQueue> EnqueueAsync(string infoHash);

    /// <summary>
    /// Dequeues the oldest pending item and marks it as processing.
    /// </summary>
    /// <returns>The dequeued item, or null if no pending items exist.</returns>
    Task<IngestionQueue?> DequeueAsync();

    /// <summary>
    /// Marks a queue item as completed or failed.
    /// </summary>
    /// <param name="id">The queue item ID.</param>
    /// <param name="errorMessage">Optional error message if the item failed.</param>
    Task MarkProcessedAsync(int id, string? errorMessage = null);

    /// <summary>
    /// Returns pending queue items up to the specified limit.
    /// </summary>
    /// <param name="limit">Maximum number of items to return (default 50).</param>
    Task<IEnumerable<IngestionQueue>> GetPendingAsync(int limit = 50);

    /// <summary>
    /// Returns counts of queue items by status.
    /// </summary>
    Task<QueueStats> GetStatsAsync();
}

/// <summary>
/// Statistics for the ingestion queue.
/// </summary>
public record QueueStats(int Pending, int Processing, int Completed, int Failed);
