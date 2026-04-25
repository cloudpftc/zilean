namespace Zilean.Shared.Features.Search;

/// <summary>
/// Tracks refresh jobs triggered by query misses or scheduled updates.
/// Durable queue-backed jobs for background hydration.
/// </summary>
public class RefreshJob
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TriggerType { get; set; } = "Scheduled"; // Scheduled, Miss, Manual

    public string? QueryFingerprint { get; set; } // For miss-triggered jobs

    public string? TargetScope { get; set; } // JSON describing what to refresh

    public string Status { get; set; } = "Pending"; // Pending, Running, Completed, Failed, Cancelled

    public string? DedupeKey { get; set; } // For preventing duplicate jobs

    public DateTime? ScheduledAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int RetryCount { get; set; }

    public string? ErrorSummary { get; set; }

    public int EntriesAdded { get; set; }

    public int EntriesUpdated { get; set; }

    public TimeSpan? ElapsedDuration => 
        StartedAt.HasValue && CompletedAt.HasValue 
            ? CompletedAt.Value - StartedAt.Value 
            : null;
}
