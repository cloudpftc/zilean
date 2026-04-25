namespace Zilean.Shared.Features.Ingestion;

/// <summary>
/// Tracks a synchronization run for a specific source type.
/// Used for audit, diagnostics, and resumability.
/// </summary>
public class SourceSyncRun
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SourceType { get; set; } = default!; // "dmm", "generic", "zurg", etc.

    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    public DateTime? EndTime { get; set; }

    public string Status { get; set; } = "Running"; // Running, Completed, Failed, Cancelled

    public int PagesProcessed { get; set; }

    public int EntriesProcessed { get; set; }

    public int EntriesAdded { get; set; }

    public int EntriesUpdated { get; set; }

    public int Errors { get; set; }

    public string? ErrorSummary { get; set; }

    public int RetryCount { get; set; }

    public TimeSpan? ElapsedDuration => EndTime.HasValue ? EndTime.Value - StartTime : null;
}
