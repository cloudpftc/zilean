namespace Zilean.Shared.Features.Search;

/// <summary>
/// Tracks search queries for audit, telemetry, and debugging.
/// Captures how queries were normalized and what results were returned.
/// </summary>
public class QueryAudit
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string RawQuery { get; set; } = default!;

    public string? NormalizedQuery { get; set; }

    public string? ContentType { get; set; } // movie, show, anime, etc.

    public int? Season { get; set; }

    public int? Episode { get; set; }

    public int? AbsoluteEpisode { get; set; }

    public int CandidateCount { get; set; }

    public int ReturnedCount { get; set; }

    public double? TopConfidence { get; set; }

    public string? ResultSummary { get; set; } // JSON summary of top results

    public bool TriggeredRefresh { get; set; }

    public string? CorrelationId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TimeSpan? ElapsedMs { get; set; }

    public string? SearchStrategy { get; set; } // What retrieval strategy was used

    public string? StalenessInfo { get; set; } // How stale the consulted state was
}
