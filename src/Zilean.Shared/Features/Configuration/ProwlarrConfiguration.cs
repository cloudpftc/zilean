namespace Zilean.Shared.Features.Configuration;

public class ProwlarrConfiguration
{
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Cron { get; set; } = "0 */6 * * *";
    public List<ProwlarrIndexer> Indexers { get; set; } = [];

    /// <summary>
    /// Master switch for the scheduled IMDb-driven Prowlarr backfill.
    /// Defaults to <c>false</c> because an unguarded run walks the entire
    /// <c>ImdbFiles</c> table, hits every Prowlarr indexer and saturates
    /// them — which makes live searches return zero results. It is only
    /// safe to enable once the scope/rate guards below are in place.
    /// </summary>
    public bool ImdbBackfillEnabled { get; set; } = false;

    /// <summary>
    /// Cron expression for the IMDb-driven backfill. Defaults to daily at
    /// 03:00 (off-peak) so a run never competes with live search traffic.
    /// </summary>
    public string ImdbBackfillCron { get; set; } = "0 3 * * *";

    /// <summary>
    /// Comma-separated Prowlarr indexer ids the backfill may query, e.g.
    /// <c>"2,4,5,6,7,9"</c>. Each id becomes a repeated <c>indexerIds</c>
    /// query parameter. EMPTY means "query every enabled Prowlarr indexer",
    /// which is the dangerous, saturation-prone mode — a warning is logged
    /// at the start of the run when this is left blank.
    /// </summary>
    public string ImdbBackfillIndexerIds { get; set; } = "";

    /// <summary>
    /// Hard cap on the number of titles actually queried per run. This is
    /// the single most important guard: it bounds how much work (and how
    /// many Prowlarr requests) a single run can do. Titles already indexed
    /// are skipped for free, so ordering by release year means successive
    /// runs naturally progress through the catalogue.
    /// </summary>
    public int ImdbBackfillMaxTitlesPerRun { get; set; } = 200;

    /// <summary>
    /// Delay in seconds applied between processed titles to keep request
    /// pressure on Prowlarr low. Replaces the previously hardcoded 5s.
    /// </summary>
    public int ImdbBackfillTitleDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Maximum number of in-flight Prowlarr requests for the backfill.
    /// Kept at 1 (fully serial) so the backfill can never saturate an
    /// indexer the way an unthrottled scraper would.
    /// </summary>
    public int ImdbBackfillMaxConcurrentRequests { get; set; } = 1;

    /// <summary>
    /// Default retry window, in days, used by the backfill when
    /// <see cref="ImdbBackfillRetryDays"/> is configured to a non-positive
    /// value. Retained as a named constant so the fallback and the default
    /// cannot drift apart.
    /// </summary>
    public const int DefaultImdbBackfillRetryDays = 30;

    /// <summary>
    /// Number of days before a title that was already queried — but produced
    /// no stored torrents — becomes eligible for another Prowlarr query. Every
    /// title that reaches a query is stamped with <c>LastQueriedAt</c> even
    /// when nothing matched; titles stamped within this window are excluded
    /// from the walk, which lets successive runs advance past titles that
    /// legitimately return zero results instead of re-walking the same head of
    /// the list every run. Defaults to 30 days.
    /// Only POSITIVE values make sense here: a value of zero or less makes a
    /// just-stamped title immediately eligible again, so the walk re-walks the
    /// same head of the list every run — the exact behaviour this window
    /// exists to prevent. The job therefore falls back to
    /// <see cref="DefaultImdbBackfillRetryDays"/> and logs a warning when this
    /// is configured to a non-positive value.
    /// </summary>
    public int ImdbBackfillRetryDays { get; set; } = DefaultImdbBackfillRetryDays;
}

public class ProwlarrIndexer
{
    public int IndexerId { get; set; }
    public string SourceName { get; set; } = "";
    public string Categories { get; set; } = "2000,5000";
    public bool Enabled { get; set; } = false;
}
