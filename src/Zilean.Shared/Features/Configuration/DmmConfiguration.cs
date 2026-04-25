namespace Zilean.Shared.Features.Configuration;

public class DmmConfiguration
{
    public bool EnableScraping { get; set; } = true;
    public bool EnableEndpoint { get; set; } = true;
    public string ScrapeSchedule { get; set; } = "0 * * * *";
    public int MinimumReDownloadIntervalMinutes { get; set; } = 30;
    public int MaxFilteredResults { get; set; } = 200;
    public double MinimumScoreMatch { get; set; } = 0.85;

    /// <summary>
    /// Boost multiplier applied to search scores for torrents with anime categories (e.g., TVAnime).
    /// Default is 1.5x. Set to 1.0 to disable anime boosting.
    /// </summary>
    public double AnimeCategoryBoost { get; set; } = 1.5;
}
