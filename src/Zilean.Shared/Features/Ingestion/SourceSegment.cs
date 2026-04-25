namespace Zilean.Shared.Features.Ingestion;

/// <summary>
/// Tracks individual pages/segments from a source for incremental, resumable ingestion.
/// Enables stale-segment invalidation and targeted refresh.
/// </summary>
public class SourceSegment
{
    [Key]
    public string Id { get; set; } = default!; // Composite: "{SourceType}:{PageOrSegmentId}"

    public string SourceType { get; set; } = default!; // "dmm", "generic", "zurg", etc.

    public string SegmentIdentifier { get; set; } = default!; // Page number, segment key, etc.

    public DateTime? LastAttempted { get; set; }

    public DateTime? LastSuccessful { get; set; }

    public string Status { get; set; } = "Pending"; // Pending, Success, Failed, Stale

    public int RetryCount { get; set; }

    public string? ErrorSummary { get; set; }

    public string? ChecksumOrVersion { get; set; } // For detecting changes

    public DateTime StaleAfter { get; set; } = DateTime.UtcNow.AddDays(7); // Configurable TTL

    public int EntryCount { get; set; } // Number of entries in this segment

    public bool IsStale => DateTime.UtcNow > StaleAfter || Status == "Stale";
}
