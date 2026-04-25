namespace Zilean.Shared.Features.Ingestion;

/// <summary>
/// Tracks ingestion checkpoints for resumable operations.
/// Allows restarting ingestion from last known good state.
/// </summary>
public class IngestionCheckpoint
{
    [Key]
    public string Id { get; set; } = default!; // Composite: "{SourceType}:{CheckpointKey}"

    public string SourceType { get; set; } = default!; // "dmm", "generic", "zurg", etc.

    public string CheckpointKey { get; set; } = default!; // e.g., "last_page", "last_cursor", "last_hash"

    public string CheckpointValue { get; set; } = default!; // The actual checkpoint value

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? Metadata { get; set; } // JSON metadata about the checkpoint
}
