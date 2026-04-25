namespace Zilean.Scraper.Features.Search.Normalization;

/// <summary>
/// Anime-specific query and title normalization service.
/// Handles romanized/native titles, episode parsing, release group stripping, and noise token removal.
/// </summary>
public class AnimeNormalizationService(
    ILogger<AnimeNormalizationService> logger,
    IOptions<AggressivePersistenceOptions> options)
{
    private readonly AggressivePersistenceOptions _options = options.Value;

    // Common anime noise tokens to strip
    private static readonly string[] NoiseTokens = 
    {
        "10bit", "8bit", "10-bit", "8-bit", "hevc", "x265", "x264", "h264", "h265",
        "aac", "ac3", "flac", "dts", "truehd",
        "bd", "bluray", "blu-ray", "web-dl", "webrip", "hdtv",
        "720p", "1080p", "2160p", "4k", "uhd",
        "dual", "audio", "multi", "subs", "subbed", "dubbed", "dub", "sub",
        "batch", "complete", "series", "collection",
        "mkv", "mp4", "avi", "mov",
        "proper", "repack", "remaster", "uncut", "uncensored",
        "nsfw", "hentai", "ecchi", "yaoi", "yuri"
    };

    // Common fansub/release group markers to strip
    private static readonly string[] ReleaseGroupMarkers =
    {
        "[", "]", "【", "】", "「", "」", "_", "-subs", "subs", "eztv", "anon", "commie",
        "horriblesubs", "horriblesubs.info", "erai-raws", " SubsPlease", "Yameii"
    };

    /// <summary>
    /// Normalize an anime title for search matching.
    /// </summary>
    public string NormalizeAnimeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;

        var normalized = title.ToLowerInvariant();

        // Remove release group tags in brackets/parentheses
        normalized = RemoveReleaseGroups(normalized);

        // Remove noise tokens
        normalized = RemoveNoiseTokens(normalized);

        // Normalize separators
        normalized = NormalizeSeparators(normalized);

        // Remove special characters except alphanumeric and spaces
        normalized = Regex.Replace(normalized, @"[^a-z0-9\s]", " ");

        // Collapse multiple spaces
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        logger.LogDebug("Normalized anime title: '{Original}' -> '{Normalized}'", title, normalized);
        return normalized;
    }

    /// <summary>
    /// Parse season and episode information from an anime title.
    /// Handles both standard (S01E05) and absolute episode numbering (Episode 105).
    /// </summary>
    public AnimeEpisodeInfo? ParseAnimeEpisode(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var lower = title.ToLowerInvariant();
        var result = new AnimeEpisodeInfo();

        // Try standard SxxExx pattern first
        var standardMatch = Regex.Match(lower, @"s(\d{1,2})e(\d{1,3})");
        if (standardMatch.Success)
        {
            result.Season = int.Parse(standardMatch.Groups[1].Value);
            result.Episode = int.Parse(standardMatch.Groups[2].Value);
            return result;
        }

        // Try absolute episode numbering patterns
        // Pattern: "Episode 105", "Ep 105", "#105"
        var absPatterns = new[]
        {
            @"episode\s*(\d{1,3})",
            @"ep\s*(\d{1,3})",
            @"#(\d{1,3})",
            @"\[(\d{1,3})\]",
            @"(\d{1,3})th\s+(?:episode|ep)"
        };

        foreach (var pattern in absPatterns)
        {
            var match = Regex.Match(lower, pattern);
            if (match.Success)
            {
                result.AbsoluteEpisode = int.Parse(match.Groups[1].Value);
                
                // Try to infer season from absolute episode (rough heuristic)
                // Most anime have ~12-26 episodes per season
                if (result.AbsoluteEpisode <= 26)
                    result.Season = 1;
                else if (result.AbsoluteEpisode <= 52)
                    result.Season = 2;
                else if (result.AbsoluteEpisode <= 78)
                    result.Season = 3;
                else
                    result.Season = (result.AbsoluteEpisode / 26) + 1;
                
                return result;
            }
        }

        // Try pack patterns: "01-12", "01~12", "01-12 (Batch)"
        var packMatch = Regex.Match(lower, @"(\d{1,2})[\-~](\d{1,2})");
        if (packMatch.Success)
        {
            result.EpisodeStart = int.Parse(packMatch.Groups[1].Value);
            result.EpisodeEnd = int.Parse(packMatch.Groups[2].Value);
            result.IsPack = true;
            return result;
        }

        return null;
    }

    /// <summary>
    /// Extract potential title aliases from an anime title.
    /// Handles Japanese romanization variants and common abbreviations.
    /// </summary>
    public List<string> ExtractAnimeAliases(string title)
    {
        var aliases = new List<string> { title };

        // Add normalized version
        aliases.Add(NormalizeAnimeTitle(title));

        // Common anime abbreviation patterns
        var abbreviations = GenerateAbbreviations(title);
        aliases.AddRange(abbreviations);

        // Remove duplicates
        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Calculate a confidence score for an anime match.
    /// Higher scores indicate better matches.
    /// </summary>
    public double CalculateAnimeMatchConfidence(
        string query, 
        string candidateTitle, 
        AnimeEpisodeInfo? queryEpisode,
        AnimeEpisodeInfo? candidateEpisode)
    {
        double score = 0.5; // Base score

        var normalizedQuery = NormalizeAnimeTitle(query);
        var normalizedCandidate = NormalizeAnimeTitle(candidateTitle);

        // Exact normalized match
        if (normalizedQuery == normalizedCandidate)
            score += 0.3;
        else if (normalizedCandidate.Contains(normalizedQuery))
            score += 0.15;
        else if (normalizedQuery.Contains(normalizedCandidate))
            score += 0.1;

        // Episode matching
        if (queryEpisode is not null && candidateEpisode is not null)
        {
            if (queryEpisode.IsPack && candidateEpisode.IsPack)
            {
                // Pack overlap check
                if (queryEpisode.EpisodeStart <= candidateEpisode.EpisodeEnd &&
                    queryEpisode.EpisodeEnd >= candidateEpisode.EpisodeStart)
                    score += 0.2;
            }
            else if (queryEpisode.Episode.HasValue && candidateEpisode.Episode.HasValue)
            {
                if (queryEpisode.Episode == candidateEpisode.Episode)
                    score += 0.2;
            }
            else if (queryEpisode.AbsoluteEpisode.HasValue && candidateEpisode.AbsoluteEpisode.HasValue)
            {
                if (queryEpisode.AbsoluteEpisode == candidateEpisode.AbsoluteEpisode)
                    score += 0.2;
            }
        }

        // Bonus for clean titles (no noise tokens remaining)
        if (!NoiseTokens.Any(t => normalizedCandidate.Contains(t.ToLower())))
            score += 0.05;

        return Math.Min(score, 1.0);
    }

    private static string RemoveReleaseGroups(string title)
    {
        var result = title;
        
        // Remove content in brackets/parentheses (common for release groups)
        result = Regex.Replace(result, @"\[[^\]]*\]", " ");
        result = Regex.Replace(result, @"\([^\)]*\)", " ");
        result = Regex.Replace(result, @"【[^】]*】", " ");
        result = Regex.Replace(result, @"「[^」]*」", " ");

        // Remove known release group suffixes
        foreach (var marker in ReleaseGroupMarkers)
        {
            if (!string.IsNullOrEmpty(marker))
                result = result.Replace(marker, " ", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private string RemoveNoiseTokens(string title)
    {
        var result = title;
        foreach (var token in NoiseTokens)
        {
            result = result.Replace(token, " ", StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    private static string NormalizeSeparators(string title)
    {
        // Replace various separators with spaces
        var result = Regex.Replace(title, @"[_\.\-\+]", " ");
        return result;
    }

    private static List<string> GenerateAbbreviations(string title)
    {
        var abbreviations = new List<string>();

        // Common anime abbreviation patterns
        var abbreviationMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "attack on titan", "aot" },
            { "shingeki no kyojin", "snk" },
            { "one piece", "op" },
            { "naruto shippuden", "ns" },
            { "my hero academia", "mha" },
            { "boku no hero academia", "bnha" },
            { "demon slayer", "ds" },
            { "kimetsu no yaiba", "kny" },
            { "jujutsu kaisen", "jjk" },
            { "tokyo ghoul", "tg" },
            { "death note", "dn" },
            { "fullmetal alchemist", "fma" },
            { "hunter x hunter", "hxh" },
            { "dragon ball z", "dbz" },
            { "dragon ball super", "dbs" }
        };

        var lowerTitle = title.ToLowerInvariant();
        foreach (var kvp in abbreviationMap)
        {
            if (lowerTitle.Contains(kvp.Key))
            {
                abbreviations.Add(kvp.Value);
                abbreviations.Add(kvp.Key);
            }
        }

        return abbreviations;
    }
}

/// <summary>
/// Parsed episode information for anime.
/// </summary>
public class AnimeEpisodeInfo
{
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public int? AbsoluteEpisode { get; set; }
    public int? EpisodeStart { get; set; } // For packs
    public int? EpisodeEnd { get; set; }   // For packs
    public bool IsPack { get; set; }
}
