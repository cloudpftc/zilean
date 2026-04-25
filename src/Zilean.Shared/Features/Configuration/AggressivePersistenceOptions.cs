namespace Zilean.Shared.Features.Configuration;

/// <summary>
/// Configuration options for aggressive Postgres-backed persistence.
/// Designed for low-RAM environments with bounded batching and durable state.
/// </summary>
public class AggressivePersistenceOptions
{
    public const string SectionName = "Zilean:AggressivePersistence";

    /// <summary>
    /// Enable aggressive persistence features. Default: true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Batch size for ingestion and refresh operations.
    /// Lower values reduce RAM usage but may increase processing time.
    /// Default: 100.
    /// </summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// Interval in minutes for incremental sync runs.
    /// Default: 15 minutes.
    /// </summary>
    public int IncrementalSyncIntervalMinutes { get; init; } = 15;

    /// <summary>
    /// TTL in minutes before a successfully processed segment is considered stale.
    /// Default: 60 minutes.
    /// </summary>
    public int StaleSegmentTtlMinutes { get; init; } = 60;

    /// <summary>
    /// Enable automatic refresh job creation on query misses.
    /// Default: true.
    /// </summary>
    public bool RefreshOnMissEnabled { get; init; } = true;

    /// <summary>
    /// Deduplication window in minutes for refresh jobs.
    /// Prevents duplicate refresh jobs for the same query within this window.
    /// Default: 30 minutes.
    /// </summary>
    public int RefreshDedupeWindowMinutes { get; init; } = 30;

    /// <summary>
    /// Enable search query auditing to PostgreSQL and local files.
    /// Default: true.
    /// </summary>
    public bool SearchAuditEnabled { get; init; } = true;

    /// <summary>
    /// Enable ranking/debug auditing for search results.
    /// Default: false (only enable for debugging).
    /// </summary>
    public bool RankingAuditEnabled { get; init; } = false;

    /// <summary>
    /// Directory for audit log files (JSONL format).
    /// Default: ./audit-logs (relative to application directory).
    /// </summary>
    public string AuditDirectory { get; init; } = "./audit-logs";

    /// <summary>
    /// Write audit logs in pretty-printed JSON format.
    /// Default: false (compact JSON for smaller files).
    /// </summary>
    public bool AuditJsonPretty { get; init; } = false;

    /// <summary>
    /// Maximum concurrent refresh jobs to process at once.
    /// Default: 2 (conservative for low-RAM environments).
    /// </summary>
    public int MaxConcurrentRefreshJobs { get; init; } = 2;

    /// <summary>
    /// Enable anime-specific normalization and matching improvements.
    /// Default: true.
    /// </summary>
    public bool AnimeNormalizationEnabled { get; init; } = true;

    /// <summary>
    /// Maximum retries per refresh job before giving up.
    /// Default: 3.
    /// </summary>
    public int MaxRetriesPerJob { get; init; } = 3;

    /// <summary>
    /// Retention days for completed refresh jobs in database.
    /// Default: 7 days.
    /// </summary>
    public int RefreshJobRetentionDays { get; init; } = 7;

    /// <summary>
    /// Enable query miss tracking and telemetry.
    /// Default: true.
    /// </summary>
    public bool QueryMissTrackingEnabled { get; init; } = true;

    /// <summary>
    /// Maximum number of query misses to track (LRU eviction).
    /// Default: 10000.
    /// </summary>
    public int MaxQueryMissesToTrack { get; init; } = 10000;
}
