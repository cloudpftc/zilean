namespace Zilean.Shared.Features.Search.Aliases;

/// <summary>
/// Maps alternate titles to canonical titles for improved search recall.
/// Supports anime romanization, translations, and other aliases.
/// </summary>
public class TitleAlias
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string RawTitle { get; set; } = default!;

    public string CanonicalTitle { get; set; } = default!;

    public string AliasType { get; set; } = "Alternate"; // Alternate, Romanized, Native, Translation, Synonym

    public string? LanguageOrScript { get; set; } // e.g., "ja", "en", "romaji", "kanji"

    public string? NormalizedTokens { get; set; } // Pre-computed normalized tokens for fast matching

    public double Confidence { get; set; } = 1.0; // Confidence in this alias mapping

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; } // For tracking alias utility
}
