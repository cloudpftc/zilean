namespace Zilean.Shared.Features.Search;

/// <summary>
/// Denormalized search-ready document for fast DB-side retrieval.
/// Contains pre-computed normalized fields and ranking features.
/// </summary>
public class SearchDocument
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SourceId { get; set; } = default!; // Original torrent/info hash or ID

    public string SourceType { get; set; } = default!; // "dmm", "generic", "zurg", etc.

    public string RawTitle { get; set; } = default!;

    public string CanonicalTitle { get; set; } = default!;

    public string? NormalizedTitle { get; set; } // Lowercased, stopwords removed, punctuation normalized

    public string? CleanedTitle { get; set; } // RTN-cleaned title without noise tokens

    public string[]? AliasTitles { get; set; } // Additional searchable titles

    public string[]? SearchTokens { get; set; } // Pre-tokenized for fast matching

    public string ContentType { get; set; } = "unknown"; // movie, show, anime, episode, pack

    public int? Year { get; set; }

    public int? Season { get; set; }

    public int? Episode { get; set; }

    public int? AbsoluteEpisode { get; set; }

    public string? ReleaseGroup { get; set; }

    public string? Resolution { get; set; }

    public string? Source { get; set; } // BluRay, WEB-DL, etc.

    public string? Codec { get; set; }

    public string? ImdbId { get; set; }

    public string? TmdbId { get; set; }

    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastRefreshedAt { get; set; } = DateTime.UtcNow;

    public bool IsStale { get; set; }

    public double QualityScore { get; set; } = 0.5; // Pre-computed quality ranking feature

    public int MatchCount { get; set; } = 0; // How many times this doc matched queries (for popularity)

    public DateTime? LastMatchedAt { get; set; }
}
