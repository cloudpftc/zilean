namespace Zilean.Shared.Features.Search;

/// <summary>
/// Tracks queries that returned no results or low-confidence results.
/// Used to trigger background refresh and improve future search.
/// </summary>
public class QueryMiss
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string NormalizedQueryFingerprint { get; set; } = default!;

    public string RawQuery { get; set; } = default!;

    public string? ContentHints { get; set; } // JSON with detected content type, season, episode, etc.

    public int MissCount { get; set; } = 1;

    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public bool RefreshTriggered { get; set; }

    public Guid? TriggeredJobId { get; set; }

    public DateTime? RefreshCompletedAt { get; set; }

    public string? ResolutionNotes { get; set; } // e.g., "still missing", "found in refresh"
}
