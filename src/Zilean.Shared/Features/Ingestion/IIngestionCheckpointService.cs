using Zilean.Shared.Features.Ingestion;

namespace Zilean.Shared.Features.Ingestion;

/// <summary>
/// Manages ingestion checkpoints for tracking progress across ingestion sources.
/// </summary>
public interface IIngestionCheckpointService
{
    /// <summary>
    /// Loads the checkpoint for a given ingestion source.
    /// </summary>
    /// <param name="source">The ingestion source identifier.</param>
    /// <returns>The checkpoint if found, null otherwise.</returns>
    Task<IngestionCheckpoint?> LoadCheckpointAsync(string source);

    /// <summary>
    /// Saves or updates a checkpoint for an ingestion source.
    /// </summary>
    /// <param name="source">The ingestion source identifier.</param>
    /// <param name="lastProcessed">The last processed item identifier.</param>
    /// <param name="status">The current status of the ingestion.</param>
    /// <param name="itemsProcessed">The number of items processed so far.</param>
    Task SaveCheckpointAsync(string source, string lastProcessed, string status, int itemsProcessed);

    /// <summary>
    /// Deletes the checkpoint for a given ingestion source.
    /// </summary>
    /// <param name="source">The ingestion source identifier.</param>
    Task DeleteCheckpointAsync(string source);
}
