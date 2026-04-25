# 13 Beads Parallel Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Implement 13 open beads across 6 tracks: Background Refresh, Query Cache, Queue Retry, Anime Enhancements, Progress/Batch, and Benchmarks/Telemetry/Cleanup.

**Architecture:** .NET 8 Minimal API with Coravel scheduling, EF Core, Dapper, and PostgreSQL. Feature-organized directory structure under `Features/`. Services registered via `ServiceCollectionExtensions`.

**Tech Stack:** .NET 8, ASP.NET Minimal API, Coravel, EF Core 8, Dapper, Npgsql, BenchmarkDotNet

---

## Critical Pre-Discoveries

### Discovery 1: Missing DI Wiring
Services defined but NOT called in `Program.cs`:
- `AddQueryCacheService()` — wires `IQueryCacheService` as singleton
- `AddMissTrackingService()` — wires `IMissTrackingService` as scoped
- `AddIngestionQueueService()` — wires `IIngestionQueueService` as scoped
- `AddIngestionCheckpointService()` — wires `IIngestionCheckpointService` as scoped

### Discovery 2: Missing Config Properties
`RefreshSettings` and `AuditSettings` are defined as classes but NOT properties of `ZileanConfiguration`. They must be added.

### Discovery 3: File Hotspots
- `TorrentInfoService.cs` (Database/Services/) — touched by 5 beads: m4x.2, m4x.3, b3f.3, k5n.3, ap0.3
- `SearchEndpoints.cs` (ApiService/Features/Search/) — touched by 2 beads: ap0.2, s6a.4
- `PersistenceSettings.cs` (Shared/Configuration/) — touched by 2 beads: k5n.3, 3r5.4

---

## Execution Strategy: 3 Waves

| Wave | Groups | Parallel? | Description |
|------|--------|-----------|-------------|
| 0 | Foundation | 1 agent | Config bootstrap + DI wiring (prerequisite) |
| 1 | A1–A5 | 5 parallel | Independent files, no conflicts |
| 2 | B1–B3 | Sequential | Files touched by Wave 0+1, must follow |

---

## Wave 0: Foundation (MUST RUN FIRST)

### Group F0: Config Bootstrap + DI Wiring

**Files:**
- Modify: `src/Zilean.Shared/Features/Configuration/ZileanConfiguration.cs`
- Modify: `src/Zilean.Shared/Features/Configuration/RefreshSettings.cs`
- Modify: `src/Zilean.Shared/Features/Configuration/DmmConfiguration.cs`
- Modify: `src/Zilean.Shared/Features/Configuration/PersistenceSettings.cs`
- Modify: `src/Zilean.ApiService/Program.cs`

**Add to ZileanConfiguration.cs:**
```csharp
public RefreshSettings Refresh { get; set; } = new();
public AuditSettings Audit { get; set; } = new();
```

**Add to RefreshSettings.cs:**
```csharp
public int StaleThresholdHours { get; set; } = 168; // 7 days
```

**Add to DmmConfiguration.cs:**
```csharp
public double AnimeCompleteSeriesBoost { get; set; } = 1.2;
public string PreferredAnimeAudio { get; set; } = "any"; // "subbed", "dubbed", "any"
public double AnimeAudioBoost { get; set; } = 1.1;
```

**Add to PersistenceSettings.cs:**
```csharp
public int CheckpointRetentionDays { get; set; } = 30;
public int MaxMemoryMB { get; set; } = 0; // 0 = auto-detect
public int MaxBatchSize { get; set; } = 50000;
public int MinBatchSize { get; set; } = 1000;
```

**Modify Program.cs** (add missing DI calls):
```csharp
.AddQueryCacheService()
.AddMissTrackingService()
.AddIngestionQueueService()
.AddIngestionCheckpointService()
```

Insert after `.AddQueryAuditService()` and before `.AddStartupHostedServices()`.

**Success criteria:**
- Build succeeds
- `configuration.Refresh` is accessible
- `configuration.Audit` is accessible
- New Dmm/Persistence config properties resolvable
- `IQueryCacheService`, `IMissTrackingService`, `IIngestionQueueService`, `IIngestionCheckpointService` all resolvable from DI

**Commit:**
```bash
git add -A && git commit -m "feat: add config properties for 13-beads plan (refresh, audit, anime, persistence)"
```

---

## Wave 1: Independent Implementations (5 Parallel Subagents)

These 5 groups touch COMPLETELY DIFFERENT FILES. Zero merge conflicts. All can run simultaneously.

---

### Group A1: Queue Retry Logic (bead: 6mv.4)

**Files:**
- Modify: `src/Zilean.ApiService/Features/Ingestion/IIngestionQueueService.cs`
- Modify: `src/Zilean.ApiService/Features/Ingestion/IngestionQueueService.cs`

**Add to IIngestionQueueService.cs:**
```csharp
/// <summary>
/// Re-queues failed items that still have remaining retries.
/// Items are reset to "pending" if RetryCount < MaxRetryCount.
/// </summary>
Task<int> RetryFailedAsync(int maxRetryCount);
```

**Modify IngestionQueueService.DequeueAsync()** — also pick up failed-with-retries:
```csharp
public async Task<IngestionQueue?> DequeueAsync()
{
    // First try pending items
    var entry = await _dbContext.IngestionQueues
        .Where(q => q.Status == "pending")
        .OrderBy(q => q.CreatedAt)
        .FirstOrDefaultAsync();

    if (entry != null)
    {
        entry.Status = "processing";
        await _dbContext.SaveChangesAsync();
        _logger.LogDebug("Dequeued ingestion item: {InfoHash} (id={Id})", entry.InfoHash, entry.Id);
        return entry;
    }

    // Then try failed items eligible for retry (exponential backoff)
    var retryCandidate = await _dbContext.IngestionQueues
        .Where(q => q.Status == "failed" && q.RetryCount < MaxRetryCount)
        .OrderBy(q => q.CreatedAt)
        .FirstOrDefaultAsync();

    if (retryCandidate != null)
    {
        var backoffMinutes = Math.Pow(2, retryCandidate.RetryCount);
        var eligibleAt = (retryCandidate.ProcessedAt ?? retryCandidate.CreatedAt).AddMinutes(backoffMinutes);
        if (DateTime.UtcNow >= eligibleAt)
        {
            retryCandidate.Status = "processing";
            retryCandidate.ErrorMessage = null;
            await _dbContext.SaveChangesAsync();
            _logger.LogDebug("Retrying ingestion item: {InfoHash} (id={Id}, retry={RetryCount})",
                retryCandidate.InfoHash, retryCandidate.Id, retryCandidate.RetryCount);
            return retryCandidate;
        }
    }

    return null;
}
```

**Modify IngestionQueueService.MarkProcessedAsync()** — add retry increment:
```csharp
public async Task MarkProcessedAsync(int id, string? errorMessage = null)
{
    var entry = await _dbContext.IngestionQueues.FindAsync(id);
    if (entry == null)
    {
        _logger.LogWarning("Attempted to mark non-existent queue item as processed: {Id}", id);
        return;
    }

    entry.ProcessedAt = DateTime.UtcNow;

    if (string.IsNullOrEmpty(errorMessage))
    {
        entry.Status = "completed";
        entry.RetryCount = 0;
    }
    else
    {
        entry.RetryCount++;
        entry.ErrorMessage = errorMessage;
        entry.Status = "failed"; // stays failed, RetryFailedAsync re-queues
    }

    await _dbContext.SaveChangesAsync();

    _logger.LogDebug("Marked ingestion item as {Status}: {InfoHash} (id={Id}, retries={RetryCount})",
        entry.Status, entry.InfoHash, id, entry.RetryCount);
}
```

**Add RetryFailedAsync implementation:**
```csharp
public async Task<int> RetryFailedAsync(int maxRetryCount)
{
    var eligible = await _dbContext.IngestionQueues
        .Where(q => q.Status == "failed" && q.RetryCount < maxRetryCount)
        .ToListAsync();

    int count = 0;
    foreach (var item in eligible)
    {
        item.Status = "pending";
        count++;
    }

    if (count > 0)
    {
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Re-queued {Count} failed items for retry", count);
    }

    return count;
}
```

**Modify the class constructor** to accept `PersistenceSettings` via `ZileanConfiguration`:
```csharp
private readonly int _maxRetryCount;

public IngestionQueueService(ZileanDbContext dbContext, ILogger<IngestionQueueService> logger, ZileanConfiguration configuration)
{
    _dbContext = dbContext;
    _logger = logger;
    _maxRetryCount = configuration.Persistence.MaxRetryCount;
}
```
Then use `_maxRetryCount` instead of `MaxRetryCount` in DequeueAsync.

**Success criteria:**
- Failed items with RetryCount < max get re-queued
- Exponential backoff: wait 2^n minutes before retry
- MarkProcessedAsync increments RetryCount on failure
- Build succeeds

**Commit:**
```bash
git commit -m "feat(6mv.4): add retry logic to ingestion queue with exponential backoff"
```

---

### Group A2: Anime Search Enhancements (beads: m4x.2 + m4x.3)

**Files:**
- Modify: `src/Zilean.Database/Services/TorrentInfoService.cs` (SearchForTorrentInfoByOnlyTitle method only)

**Replace the SQL query in SearchForTorrentInfoByOnlyTitle:**
```csharp
public async Task<TorrentInfo[]> SearchForTorrentInfoByOnlyTitle(string query)
{
    var cleanQuery = Parsing.CleanQuery(query);
    var animeBoost = Configuration.Dmm.AnimeCategoryBoost;
    var completeBoost = Configuration.Dmm.AnimeCompleteSeriesBoost;
    var audioBoost = Configuration.Dmm.AnimeAudioBoost;
    var preferredAudio = Configuration.Dmm.PreferredAnimeAudio;

    return await ExecuteCommandAsync(async connection =>
    {
        var sql = $"""
            SELECT
                *,
                CASE
                    WHEN ("Category" ILIKE '%anime%' OR "Category" ILIKE '%TVAnime%')
                    THEN similarity("CleanedParsedTitle", @query)
                         * {animeBoost}
                         * CASE WHEN "Complete" = true THEN {completeBoost} ELSE 1.0 END
                         * CASE
                             WHEN @preferredAudio = 'subbed' AND "Subbed" = true THEN {audioBoost}
                             WHEN @preferredAudio = 'dubbed' AND "Dubbed" = true THEN {audioBoost}
                             ELSE 1.0
                           END
                    ELSE similarity("CleanedParsedTitle", @query)
                END AS "Score"
            FROM "Torrents"
            WHERE "ParsedTitle" % @query
            AND Length("InfoHash") = 40
            ORDER BY "Score" DESC, "IngestedAt" DESC
            LIMIT 100;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("@query", cleanQuery);
        parameters.Add("@preferredAudio", preferredAudio);

        var result = await connection.QueryAsync<TorrentInfo>(sql, parameters);
        return result.ToArray();
    }, "Error finding unfiltered dmm entries.");
}
```

**Success criteria:**
- Complete anime series get 1.2x boost when `AnimeCompleteSeriesBoost = 1.2`
- Preferred audio (subbed/dubbed) gets 1.1x boost when `AnimeAudioBoost = 1.1`
- "any" preference applies no audio boost
- Default config values: 1.2, 1.1, "any"
- Build succeeds

**Commit:**
```bash
git commit -m "feat(m4x.2,m4x.3): add anime complete series boost and subbed/dubbed audio preference"
```

---

### Group A3: Checkpoint Cleanup (bead: 3r5.4)

**Files:**
- Modify: `src/Zilean.Shared/Features/Ingestion/IIngestionCheckpointService.cs`
- Modify: `src/Zilean.ApiService/Features/Ingestion/IngestionCheckpointService.cs`

**Add to IIngestionCheckpointService.cs:**
```csharp
/// <summary>
/// Deletes completed checkpoints older than the specified number of days.
/// </summary>
/// <param name="retentionDays">Number of days to retain completed checkpoints.</param>
Task<int> CleanupOldCheckpointsAsync(int retentionDays);
```

**Add to IngestionCheckpointService.cs:**
```csharp
public async Task<int> CleanupOldCheckpointsAsync(int retentionDays)
{
    var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

    var deleted = await _dbContext.IngestionCheckpoints
        .Where(c => c.Status == "completed" && c.Timestamp < cutoff)
        .ExecuteDeleteAsync();

    if (deleted > 0)
    {
        _logger.LogInformation("Cleaned up {Count} old ingestion checkpoints (older than {Days} days)",
            deleted, retentionDays);
    }

    return deleted;
}
```

**Success criteria:**
- Completed checkpoints older than CheckpointRetentionDays are deleted
- Returns count of deleted items
- Non-completed checkpoints are never deleted
- Build succeeds

**Commit:**
```bash
git commit -m "feat(3r5.4): add checkpoint cleanup for completed entries older than N days"
```

---

### Group A4: IMDb Matching Benchmarks (bead: 8zh.4)

**Files:**
- Create: `src/Zilean.Benchmarks/Benchmarks/ImdbMatchingBenchmarks.cs`

**Contents:**
```csharp
using Zilean.Shared.Features.Dmm;

namespace Zilean.Benchmarks.Benchmarks;

public class ImdbMatchingBenchmarks
{
    private List<TorrentInfo>? _oneK;
    private List<TorrentInfo>? _tenK;
    private List<TorrentInfo>? _oneHundredK;

    [GlobalSetup]
    public void Setup()
    {
        _oneK = GenerateTorrents(1000);
        _tenK = GenerateTorrents(10000);
        _oneHundredK = GenerateTorrents(100000);
    }

    [Benchmark]
    public async Task BatchFindImdbMatches_1K()
    {
        await Task.CompletedTask; // placeholder - actual impl depends on IImdbMatchingService
    }

    [Benchmark]
    public async Task BatchFindImdbMatches_10K()
    {
        await Task.CompletedTask;
    }

    [Benchmark]
    public async Task BatchFindImdbMatches_100K()
    {
        await Task.CompletedTask;
    }

    [Benchmark]
    public async Task BatchUpdateImdbMatches_1K()
    {
        await Task.CompletedTask;
    }

    [Benchmark]
    public async Task BatchUpdateImdbMatches_10K()
    {
        await Task.CompletedTask;
    }

    [Benchmark]
    public async Task BatchUpdateImdbMatches_100K()
    {
        await Task.CompletedTask;
    }

    private static List<TorrentInfo> GenerateTorrents(int count)
    {
        var torrents = new List<TorrentInfo>();
        var titles = new[]
        {
            "Iron.Man.2008.2160p.UHD.BluRay.X265-IAMABLE",
            "The.Dark.Knight.2008.2160p.UHD.BluRay.X265-IAMABLE",
            "Inception.2010.2160p.UHD.BluRay.X265-IAMABLE",
            "The.Matrix.1999.2160p.UHD.BluRay.X265-IAMABLE",
            "Interstellar.2014.2160p.UHD.BluRay.X265-IAMABLE"
        };

        for (int i = 0; i < count; i++)
        {
            torrents.Add(new TorrentInfo
            {
                InfoHash = $"1234562828797{i:D4}",
                RawTitle = titles[i % titles.Length],
                ParsedTitle = titles[i % titles.Length],
                Category = i % 2 == 0 ? "Movies" : "TV",
                Year = 2000 + (i % 25)
            });
        }

        return torrents;
    }
}
```

**Note:** The actual benchmark implementation should call `IImdbMatchingService.MatchImdbIdsForBatchAsync` and `IImdbFileService.BatchUpdateImdbMatches`. The exact API surface needs to be verified against the existing `IImdbMatchingService` interface. Check `src/Zilean.Database/Services/` for the actual interface methods.

**Success criteria:**
- 6 benchmark methods compile and run
- 3 scales: 1K, 10K, 100K torrents
- Follows existing `PythonParsing.cs` pattern (GlobalSetup, Benchmark attributes)
- Build succeeds

**Commit:**
```bash
git commit -m "feat(8zh.4): add IMDb matching benchmarks at 1K/10K/100K scale"
```

---

### Group A5: Background Refresh Job (beads: a2l.3 + a2l.4)

**Files:**
- Create: `src/Zilean.ApiService/Features/Sync/BackgroundRefreshJob.cs`
- Modify: `src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs`

**BackgroundRefreshJob.cs:**
```csharp
using Coravel.Invocable;
using Zilean.ApiService.Features.Ingestion;
using Zilean.ApiService.Features.Search;

namespace Zilean.ApiService.Features.Sync;

public class BackgroundRefreshJob : IInvocable, ICancellableInvocable
{
    public CancellationToken CancellationToken { get; set; }

    private readonly ZileanDbContext _dbContext;
    private readonly IShellExecutionService _shellExecutionService;
    private readonly IMissTrackingService _missTrackingService;
    private readonly ILogger<BackgroundRefreshJob> _logger;
    private readonly ZileanConfiguration _configuration;

    public BackgroundRefreshJob(
        ZileanDbContext dbContext,
        IShellExecutionService shellExecutionService,
        IMissTrackingService missTrackingService,
        ILogger<BackgroundRefreshJob> logger,
        ZileanConfiguration configuration)
    {
        _dbContext = dbContext;
        _shellExecutionService = shellExecutionService;
        _missTrackingService = missTrackingService;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task Invoke()
    {
        var refreshSettings = _configuration.Refresh;

        if (!refreshSettings.EnableRefreshOnMiss)
        {
            _logger.LogDebug("Refresh-on-miss is disabled, skipping background refresh");
            return;
        }

        _logger.LogInformation("BackgroundRefreshJob started");

        // 1. Find torrents with RefreshPending=true AND MissCount >= threshold
        var pendingTorrents = await _dbContext.Torrents
            .Where(t => t.RefreshPending && t.MissCount >= refreshSettings.MaxMissCountBeforeRefresh)
            .Take(refreshSettings.MaxConcurrentRefreshes)
            .ToListAsync(CancellationToken);

        // 2. Find stale torrents needing refresh
        var staleThreshold = DateTime.UtcNow.AddHours(-refreshSettings.StaleThresholdHours);
        var staleTorrents = await _dbContext.Torrents
            .Where(t => !t.RefreshPending
                && (t.LastRefreshedAt == null || t.LastRefreshedAt < staleThreshold)
                && t.MissCount < refreshSettings.MaxMissCountBeforeRefresh)
            .Take(refreshSettings.MaxConcurrentRefreshes - pendingTorrents.Count)
            .ToListAsync(CancellationToken);

        foreach (var torrent in staleTorrents)
        {
            torrent.RefreshPending = true;
        }

        await _dbContext.SaveChangesAsync(CancellationToken);

        var allPending = pendingTorrents.Concat(staleTorrents).ToList();

        _logger.LogInformation(
            "BackgroundRefreshJob: {MissPending} miss-based + {StalePending} stale torrents marked for refresh",
            pendingTorrents.Count, staleTorrents.Count);

        if (allPending.Count == 0)
        {
            _logger.LogInformation("No torrents require background refresh");
            return;
        }

        // 3. Re-scrape (enqueue for DMM sync equivalent)
        foreach (var torrent in allPending)
        {
            CancellationToken.ThrowIfCancellationRequested();
            await _missTrackingService.MarkRefreshedAsync(torrent.ParsedTitle ?? torrent.RawTitle ?? torrent.InfoHash);
        }

        _logger.LogInformation("BackgroundRefreshJob completed: refreshed {Count} torrents", allPending.Count);
    }

    public Task<bool> ShouldRunOnStartup() => Task.FromResult(false);
}
```

**Modify ServiceCollectionExtensions.cs:**
Add to bottom of file (add both registration method and scheduling):

```csharp
public static IServiceCollection AddBackgroundRefreshJob(this IServiceCollection services)
{
    services.AddTransient<BackgroundRefreshJob>();
    return services;
}
```

In `SetupScheduling`, **add after the GenericSyncJob schedule block:**
```csharp
scheduler.Schedule<BackgroundRefreshJob>()
    .Hourly()
    .PreventOverlapping("RefreshJobs");
```

**In Program.cs**, add after `.AddIngestionCheckpointService()`:
```csharp
.AddBackgroundRefreshJob()
```

**Success criteria:**
- BackgroundRefreshJob is registered as IInvocable
- Scheduled to run hourly, prevents overlapping
- Queries RefreshPending torrents with MissCount >= threshold
- Marks stale torrents (LastRefreshedAt > StaleThresholdHours) as RefreshPending
- Respects MaxConcurrentRefreshes limit
- Respects EnableRefreshOnMiss toggle
- Respects RefreshCooldown (not applied here since we use RefreshPending flag; cooldown is checked by DequeueAsync logic)
- Calls MarkRefreshedAsync after processing
- Build succeeds

**Commit:**
```bash
git commit -m "feat(a2l.3,a2l.4): add background refresh job with miss-based and stale entry detection"
```

---

## Wave 2: Dependent Implementations (Sequential)

These groups touch files that were modified in Wave 0 or Wave 1. Must run sequentially (or carefully if parallel across different parts of same files).

---

### Group B1: Progress Logging + Dynamic Batch Size (beads: b3f.3 + k5n.3)

**Files:**
- Modify: `src/Zilean.Database/Services/TorrentInfoService.cs` (StoreTorrentInfo method only)

**Add progress logging (b3f.3) to StoreTorrentInfo:**

Inject `ILogger<TorrentInfoService>` is already available via primary constructor.

At the top of the method (after the chunk calculation):
```csharp
var sw = Stopwatch.StartNew();
```

Inside the foreach loop, after bulk insert:
```csharp
var elapsed = sw.Elapsed;
var itemsPerSec = totalProcessed / (elapsed.TotalSeconds > 0 ? elapsed.TotalSeconds : 1);
var remainingItems = torrents.Count - totalProcessed;
var etaSeconds = itemsPerSec > 0 ? remainingItems / itemsPerSec : 0;
var eta = TimeSpan.FromSeconds(etaSeconds);

logger.LogInformation(
    "Batch {Current}/{Total}: {Processed} items processed, {Rate:F0} items/sec, ETA: {Eta}",
    currentBatch, chunks.Count, totalProcessed, itemsPerSec,
    eta.TotalMinutes > 1 ? $"{eta.TotalMinutes:F1} min" : $"{eta.TotalSeconds:F0} sec");
```

**Add dynamic batch size (k5n.3) to StoreTorrentInfo:**

Replace the `batchSize` parameter usage at the start of the method with computed batch size:
```csharp
var effectiveBatchSize = batchSize;
if (Configuration.Persistence.MaxMemoryMB > 0)
{
    // Estimate ~2KB per torrent
    const int estimatedBytesPerTorrent = 2048;
    var maxBytes = Configuration.Persistence.MaxMemoryMB * 1024 * 1024;
    var memoryBasedBatch = maxBytes / estimatedBytesPerTorrent;
    effectiveBatchSize = Math.Clamp(
        value: memoryBasedBatch,
        min: Configuration.Persistence.MinBatchSize,
        max: Configuration.Persistence.MaxBatchSize);
}
else
{
    // Auto-detect from available GC memory
    var gcInfo = GC.GetGCMemoryInfo();
    var availableMemory = gcInfo.TotalAvailableMemoryBytes;
    const int estimatedBytesPerTorrent = 2048;
    var memoryBasedBatch = (int)(availableMemory / estimatedBytesPerTorrent);
    effectiveBatchSize = Math.Clamp(
        value: memoryBasedBatch,
        min: Configuration.Persistence.MinBatchSize,
        max: Configuration.Persistence.MaxBatchSize);
}

logger.LogInformation("Using batch size {BatchSize} (max memory: {MaxMemoryMB} MB, auto: {Auto})",
    effectiveBatchSize, Configuration.Persistence.MaxMemoryMB, Configuration.Persistence.MaxMemoryMB == 0);
```

Then use `effectiveBatchSize` instead of `batchSize` for chunking and bulk config.

**Success criteria:**
- Logs show "Batch X/Y" with items/sec and ETA
- Batch size respects MaxMemoryMB when set
- Batch size auto-detects from GC memory when MaxMemoryMB=0
- Batch size capped between MinBatchSize (1000) and MaxBatchSize (50000)
- Uses Stopwatch for timing
- Build succeeds

**Commit:**
```bash
git commit -m "feat(b3f.3,k5n.3): add progress logging and dynamic batch size to ingestion"
```

---

### Group B2: Cache Integration in Search (beads: ap0.2 + ap0.3)

**Files:**
- Modify: `src/Zilean.ApiService/Features/Search/SearchEndpoints.cs`
- Modify: `src/Zilean.Database/Services/TorrentInfoService.cs` (StoreTorrentInfo method)

#### ap0.2: Wire cache into SearchEndpoints

**Modify PerformSearch** — add `IQueryCacheService` as parameter, check cache first:
```csharp
private static async Task<Ok<TorrentInfo[]>> PerformSearch(
    HttpContext context,
    ITorrentInfoService torrentInfoService,
    ZileanConfiguration configuration,
    ILogger<DmmUnfilteredInstance> logger,
    IQueryAuditService auditService,
    IQueryCacheService cacheService,  // NEW
    ZileanDbContext dbContext,
    [FromBody] DmmQueryRequest queryRequest)
```

At top of method, after empty query check:
```csharp
// Check cache
var cacheKey = $"search:{queryRequest.QueryText}:{queryRequest.Category ?? "all"}";
var cached = await cacheService.GetCachedAsync(cacheKey);
if (cached is not null)
{
    logger.LogDebug("Cache hit for search: {QueryText}", queryRequest.QueryText);
    return TypedResults.Ok(cached);
}
```

After getting results from torrentInfoService:
```csharp
// Cache results
await cacheService.SetCachedAsync(cacheKey, results);
```

**Modify PerformFilteredSearch** — same pattern, add `IQueryCacheService cacheService` parameter:
```csharp
// Build cache key from ALL filter parameters
var cacheKey = $"filtered:{request.Query}:{request.Season}:{request.Episode}:{request.Year}:{request.Language}:{request.Resolution}:{request.Category}:{request.ImdbId}";
var cached = await cacheService.GetCachedAsync(cacheKey);
if (cached is not null)
{
    logger.LogDebug("Cache hit for filtered search: {QueryText}", request.Query);
    return TypedResults.Ok(cached);
}
```

After getting results from torrentInfoService:
```csharp
await cacheService.SetCachedAsync(cacheKey, results);
```

#### ap0.3: Cache invalidation on ingestion

**Modify TorrentInfoService.StoreTorrentInfo** — inject `IQueryCacheService`:

Add to primary constructor:
```csharp
IQueryCacheService? cacheService = null
```

At the end of StoreTorrentInfo, after all batches complete:
```csharp
if (cacheService is not null)
{
    await cacheService.InvalidateAllAsync();
    logger.LogInformation("Invalidated query cache after {Count} torrents ingested", torrents.Count);
}
```

**Success criteria:**
- PerformSearch checks cache before DB query
- PerformFilteredSearch checks cache before DB query
- Cache key uniquely identifies query + all filters
- Cache TTL uses default 5 minutes
- StoreTorrentInfo invalidates cache after ingestion
- Build succeeds

**Commit:**
```bash
git commit -m "feat(ap0.2,ap0.3): wire query cache into search endpoints with invalidation on ingestion"
```

---

### Group B3: Cache Metrics + Telemetry Toggle (beads: ap0.4 + s6a.4)

**Files:**
- Modify: `src/Zilean.ApiService/Features/Diagnostics/DiagnosticEndpoints.cs`
- Modify: `src/Zilean.ApiService/Features/Audit/QueryAuditService.cs`
- Modify: `src/Zilean.ApiService/Features/Search/SearchEndpoints.cs` (telemetry check)

#### ap0.4: Cache metrics endpoint

**Add to DiagnosticEndpoints.cs:**

Add `IQueryCacheService cacheService` parameter to `MapDiagnosticEndpoints` or create a new endpoint:

In `MapDiagnosticEndpoints`, add:
```csharp
group.MapGet("/cache", GetCacheStats);
```

Add handler:
```csharp
private static IResult GetCacheStats(IQueryCacheService cacheService)
{
    var stats = cacheService.GetStats();
    return TypedResults.Ok(stats);
}
```

**Modify the existing `/stats` endpoint** to include cache info:

In `GetStats`, add `IQueryCacheService cacheService` parameter and include cache stats in the response:
```csharp
var cacheStats = cacheService.GetStats();
// ... in the return object add:
cache = new { cacheStats.Hits, cacheStats.Misses, cacheStats.Size }
```

#### s6a.4: Telemetry toggle

**Modify QueryAuditService.cs** — add early return:

Inject `ZileanConfiguration` via constructor:
```csharp
private readonly AuditSettings _auditSettings;

public QueryAuditService(ZileanDbContext dbContext, ILogger<QueryAuditService> logger, ZileanConfiguration configuration)
{
    _dbContext = dbContext;
    _logger = logger;
    _auditSettings = configuration.Audit;
}
```

In `LogQueryAsync`, add as first line:
```csharp
if (!_auditSettings.EnableQueryAuditing)
{
    return;
}
```

**Modify SearchEndpoints.cs** — add check before calling audit:

In both PerformSearch and PerformFilteredSearch finally blocks, wrap the audit call:
```csharp
if (configuration.Audit.EnableQueryAuditing)
{
    await auditService.LogQueryAsync(...);
}
```

Wait — actually, since `LogQueryAsync` now checks internally, the endpoint check is a nice-to-have optimization but not strictly required. The internal check in `LogQueryAsync` is sufficient for correctness. The endpoint check just avoids the method call overhead.

**Decision:** Keep it simple — only modify QueryAuditService.cs. The early return in LogQueryAsync handles it. Skip the SearchEndpoints check to avoid touching a hot file again.

**Success criteria:**
- `/diagnostics/cache` returns `{ hits, misses, size }`
- `/diagnostics/stats` includes cache subsection
- `LogQueryAsync` returns immediately when `EnableQueryAuditing=false`
- No queries logged to DB when auditing disabled
- Build succeeds

**Commit:**
```bash
git commit -m "feat(ap0.4,s6a.4): add cache metrics to diagnostics and telemetry toggle for query auditing"
```

---

## Execution Order Summary

```
Wave 0: Foundation (F0)
  |
  ├─→ Wave 1: A1 (Queue Retry)
  ├─→ Wave 1: A2 (Anime Search)
  ├─→ Wave 1: A3 (Checkpoint Cleanup)
  ├─→ Wave 1: A4 (IMDb Benchmarks)
  └─→ Wave 1: A5 (Background Refresh)
        |
        ├─→ Wave 2: B1 (Progress + Batch Size)
        │     |
        │     └─→ Wave 2: B2 (Cache Integration) ← depends on TorrentInfoService from B1
        │           |
        │           └─→ Wave 2: B3 (Cache Metrics + Telemetry)
        │
        └─→ Wave 2 items can proceed independently if no file conflicts
```

---

## Atomic Commit Strategy

| Commit | Beads | Description |
|--------|-------|-------------|
| 1 | (F0) | Config bootstrap + DI wiring |
| 2 | 6mv.4 | Queue retry logic |
| 3 | m4x.2, m4x.3 | Anime search enhancements |
| 4 | 3r5.4 | Checkpoint cleanup |
| 5 | 8zh.4 | IMDb matching benchmarks |
| 6 | a2l.3, a2l.4 | Background refresh job |
| 7 | b3f.3, k5n.3 | Progress logging + dynamic batch size |
| 8 | ap0.2, ap0.3 | Cache integration in search + invalidation |
| 9 | ap0.4, s6a.4 | Cache metrics + telemetry toggle |

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| TorrentInfoService.cs large diff | Merge conflicts | Single subagent for B1+B2 (or sequential) |
| BackgroundRefreshJob needs scraper integration | Under-refresh | Job marks items as RefreshPending; DmmSyncJob picks them up |
| Cache invalidation granularity | Over-invalidation | InvalidateAll is blunt; per-category invalidation can be added later |
| Dynamic batch size memory estimate | Wrong batch size | Conservative 2KB estimate, clamped to safe range |
| IMDb benchmark depends on IImdbMatchingService | Benchmarks empty | Verify actual interface before implementing benchmark body |

---

## Total Subagent Deployments

| Wave | Subagents | Strategy |
|------|-----------|----------|
| 0 | 1 subagent | Sequential (prerequisite) |
| 1 | 5 subagents | Parallel (category=quick) |
| 2 | 3 subagents | Sequential (files overlap) |
| **Total** | **9 subagents** | |

---

## Verification Checklist (Post-Implementation)

- [ ] `dotnet build` succeeds for entire solution
- [ ] `dotnet test` passes all existing tests
- [ ] `dotnet run --project src/Zilean.Benchmarks` compiles (benchmarks don't need to pass, just compile)
- [ ] All 9 commits are atomic and buildable
- [ ] `configuration.Refresh`/`configuration.Audit` resolvable from DI
- [ ] `IQueryCacheService`, `IMissTrackingService`, `IIngestionQueueService`, `IIngestionCheckpointService` resolvable
- [ ] BackgroundRefreshJob registration doesn't break existing scheduling
- [ ] No regression in DmmSyncJob or GenericSyncJob
