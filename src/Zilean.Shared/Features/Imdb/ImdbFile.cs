namespace Zilean.Shared.Features.Imdb;

public class ImdbFile
{
    [Key]
    public string ImdbId { get; set; } = default!;
    public string? Category { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public bool Adult { get; set; }
    public int Year { get; set; }

    /// <summary>
    /// Records that a Prowlarr query was ISSUED for this title, regardless of
    /// whether anything matched. The IMDb-driven backfill stamps this on every
    /// title that reaches a Prowlarr query so the walk can advance past titles
    /// that legitimately return zero results — without it, such titles are
    /// re-queried on every run and coverage never moves past the head of the
    /// list. A title with a null stamp, or one stamped longer ago than the
    /// configured retry window, becomes eligible again.
    /// </summary>
    public DateTime? LastQueriedAt { get; set; }
}
