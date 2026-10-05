namespace Zilean.Shared.Features.Configuration;

public class DmmConfiguration
{
    public bool EnableScraping { get; set; } = true;
    public bool EnableEndpoint { get; set; } = true;
    public string ScrapeSchedule { get; set; } = "0 * * * *";
    public int MinimumReDownloadIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Git repository holding the DMM public hashlists. Fetched incrementally.
    /// </summary>
    public string RepositoryUrl { get; set; } = "https://github.com/debridmediamanager/hashlists.git";

    /// <summary>
    /// Branch to track in the DMM hashlists repository.
    /// </summary>
    public string RepositoryBranch { get; set; } = "main";

    /// <summary>
    /// Persistent working copy of the DMM hashlists repository. Must live on a
    /// persisted volume so hourly syncs are incremental fetches rather than full
    /// re-downloads (the repo is ~1.4 GB; a full zipball pull every hour both
    /// wastes bandwidth and trips GitHub's unauthenticated rate limit).
    /// </summary>
    public string RepositoryDirectory { get; set; } = "/app/data/DMMHashlists";

    public int MaxFilteredResults { get; set; } = 200;
    public double MinimumScoreMatch { get; set; } = 0.85;

    /// <summary>
    /// Boost multiplier applied to search scores for torrents with anime categories (e.g., TVAnime).
    /// Default is 1.5x. Set to 1.0 to disable anime boosting.
    /// </summary>
    public double AnimeCategoryBoost { get; set; } = 1.5;

    /// <summary>
    /// Boost multiplier for anime torrents marked as complete series. Default 2.0x.
    /// </summary>
    public double AnimeCompleteSeriesBoost { get; set; } = 2.0;

    /// <summary>
    /// Preferred audio type for anime: "subbed", "dubbed", or "any". Default "any".
    /// </summary>
    public string AnimeAudioPreference { get; set; } = "any";
}
