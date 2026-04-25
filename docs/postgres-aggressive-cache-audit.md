# Postgres-Aggressive Cache Architecture Audit

**Document Version:** 1.0  
**Date:** 2025-04-25  
**Project:** Zilean (iPromKnight/zilean)  
**Scope:** Repo-level changes to Zilean only  

---

## Executive Summary

This audit examines the current Zilean codebase to identify opportunities for implementing a Postgres-first architecture optimized for low-RAM environments. The goal is to shift from transient in-memory processing toward durable, checkpointed, and auditable persistence patterns that improve search recall, freshness, and operational visibility.

### Key Findings

1. **Current State:** Zilean already uses Postgres as its primary data store but lacks:
   - Checkpoint-driven ingestion
   - Segment/page state tracking with staleness detection
   - Query telemetry and miss tracking
   - Refresh job queues
   - Structured audit logging to disk
   - Anime-specific normalization infrastructure

2. **RAM Pressure Points:**
   - Bulk torrent storage loads entire batches into memory
   - Python RTN parsing holds batch results in memory
   - No bounded streaming for large ingestion operations
   - ParsedPages loaded entirely into ConcurrentDictionary

3. **Search Limitations:**
   - Simple trigram similarity on `CleanedParsedTitle`
   - No alias expansion or title canonicalization
   - No query normalization beyond stopword removal
   - No anime-specific parsing logic
   - Limited ranking features

4. **Observability Gaps:**
   - Minimal structured logging
   - No append-only audit files
   - No query miss tracking
   - Limited freshness diagnostics
   - No correlation IDs for tracing

---

## 1. Current Ingestion Flow(s)

### Primary Ingestion: DMM Sync

**Entry Point:** `Zilean.ApiService.Features.Sync.DmmSyncJob`  
**Execution:** Shell command → `Zilean.Scraper.Features.Commands.DmmSyncCommand` → `DmmScraping.Execute()`

**Flow:**
```
DmmSyncJob (scheduled via cron) 
  ↓
DmmScraping.Execute()
  ↓
Download DMM file to temp path
  ↓
Retrieve existing ParsedPages from DB
  ↓
Filter out already-processed pages
  ↓
Process new files via DmmFileEntryProcessor
  ↓
Parse HTML → extract hashlist JSON → parse torrents
  ↓
RTN (Python) parsing for metadata extraction
  ↓
Bulk insert into Torrents table
  ↓
Update ParsedPages and ImportMetadata
  ↓
VACUUM indexes
```

**Key Files:**
- `/workspace/src/Zilean.ApiService/Features/Sync/DmmSyncJob.cs`
- `/workspace/src/Zilean.Scraper/Features/Commands/DmmSyncCommand.cs`
- `/workspace/src/Zilean.Scraper/Features/Ingestion/Dmm/DmmScraping.cs`
- `/workspace/src/Zilean.Scraper/Features/Ingestion/Processing/DmmFileEntryProcessor.cs`

**Pain Points:**
1. **No checkpoints:** If interrupted mid-sync, must restart from beginning
2. **All-or-nothing page processing:** No partial page progress tracking
3. **No staleness detection:** Pages never invalidated/refreshed
4. **Memory retention:** `ConcurrentDictionary<string, int>` for all parsed pages
5. **No retry metadata:** Failed pages not tracked for retry

### Secondary Ingestion: Generic Scraping

**Entry Point:** `GenericSyncJob` → `GenericSyncCommand`  
**Sources:** Zurg instances, other Zilean instances, generic endpoints

**Flow:**
```
GenericSyncJob (scheduled via cron)
  ↓
Kubernetes service discovery OR configured endpoints
  ↓
HTTP scrape → StreamedEntryProcessor
  ↓
RTN parsing → Bulk insert
```

**Key Files:**
- `/workspace/src/Zilean.ApiService/Features/Sync/GenericSyncJob.cs`
- `/workspace/src/Zilean.Scraper/Features/Commands/GenericSyncCommand.cs`
- `/workspace/src/Zilean.Scraper/Features/Ingestion/Endpoints/GenericIngestionScraping.cs`
- `/workspace/src/Zilean.Scraper/Features/Ingestion/Processing/GenericProcessor.cs`

**Pain Points:**
1. Same issues as DMM sync
2. No source-specific checkpointing
3. No deduplication across sources

### Tertiary Ingestion: IMDB Metadata

**Entry Point:** `ResyncImdbCommand` → `ImdbFileDownloader` → `ImdbFileProcessor`

**Flow:**
```
Download IMDB title.basics.tsv.gz
  ↓
Stream process → ImdbFiles table
  ↓
Lucene index (optional)
```

**Key Files:**
- `/workspace/src/Zilean.Scraper/Features/Commands/ResyncImdbCommand.cs`
- `/workspace/src/Zilean.Scraper/Features/Imdb/ImdbFileDownloader.cs`
- `/workspace/src/Zilean.Scraper/Features/Imdb/ImdbFileProcessor.cs`

---

## 2. Current Scheduler/Background Workers

### Coravel-based Scheduling

**Configuration:** Cron schedules defined in `DmmConfiguration` and `IngestionConfiguration`

```csharp
// DMM: "0 * * * *" (hourly)
// Generic: "0 0 * * *" (daily)
public class DmmConfiguration
{
    public string ScrapeSchedule { get; set; } = "0 * * * *";
}
```

**Key Files:**
- `/workspace/src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs` (not fully examined)
- `/workspace/src/Zilean.ApiService/Features/Sync/*.cs`

**Pain Points:**
1. **No queue-backed jobs:** Fire-and-forget execution
2. **No job deduplication:** Concurrent runs possible
3. **No failure recovery:** Failed jobs not retried automatically
4. **No priority system:** All jobs equal priority
5. **No refresh-on-miss trigger:** Search misses don't trigger ingestion

---

## 3. Current DB Models/Schema and Postgres Usage

### Entity Framework Core DbContext

**File:** `/workspace/src/Zilean.Database/ZileanDbContext.cs`

**Tables:**
1. **Torrents** (`TorrentInfo`)
   - Primary search table
   - Fields: InfoHash (PK), RawTitle, ParsedTitle, NormalizedTitle, CleanedParsedTitle, Year, Resolution, Seasons[], Episodes[], Languages[], Category, ImdbId (FK), IngestedAt, +30 more metadata fields
   - Indexes: Trigram on CleanedParsedTitle, B-tree on various fields

2. **ImdbFiles** (`ImdbFile`)
   - IMDB metadata
   - Fields: ImdbId (PK), Title, Category, Year, Adult
   - Joined in search queries

3. **ParsedPages** (`ParsedPages`)
   - Tracks processed DMM pages
   - Fields: Page (PK), EntryCount
   - **Issue:** No timestamp, no checksum, no status, no retry count

4. **ImportMetadata** (`ImportMetadata`)
   - Key-value store for import state
   - Fields: Key (PK), Value (JSONB)
   - Used for DmmLastImport, ImdbLastImport

5. **BlacklistedItems** (`BlacklistedItem`)
   - Blacklisted info hashes
   - Fields: InfoHash (PK), Reason, CreatedAt

### PostgreSQL Functions

**Location:** `/workspace/src/Zilean.Database/Functions/`

**Key Functions:**
1. **search_torrents_meta (V5)** - Main search function
   - Uses `pg_trgm.similarity_threshold`
   - Trigram similarity on `CleanedParsedTitle`
   - Joins ImdbFiles
   - Filters: season, episode, year, language, resolution, category, imdbId
   - Returns scored results ordered by similarity + IngestedAt

2. **search_imdb_meta (V3)** - IMDB search
   - Trigram similarity on Title
   - Category filtering
   - Year range filtering

**Indexes:**
- Trigram indexes on CleanedParsedTitle, Title
- B-tree on InfoHash, ImdbId, Category, Year, etc.

**Pain Points:**
1. **No materialized views:** Search always computes on-the-fly
2. **No token tables:** No pre-computed normalized tokens
3. **No alias tables:** No title alias expansion
4. **No query telemetry tables:** Can't track what users search for
5. **No refresh job tables:** No durable job queue
6. **No segment state tables:** Can't track freshness per segment
7. **No checkpoint tables:** Only simple key-value in ImportMetadata

---

## 4. Current Search Path and Ranking Path

### Search Endpoint Flow

**File:** `/workspace/src/Zilean.ApiService/Features/Search/SearchEndpoints.cs`

**Unfiltered Search:**
```
POST /dmm/search
  ↓
SearchEndpoints.PerformSearch()
  ↓
ITorrentInfoService.SearchForTorrentInfoByOnlyTitle(query)
  ↓
Direct SQL: WHERE "ParsedTitle" % @query (trigram)
  ↓
LIMIT 100
  ↓
Return TorrentInfo[]
```

**Filtered Search:**
```
GET /dmm/filtered?query=&season=&episode=&year=&...
  ↓
SearchEndpoints.PerformFilteredSearch()
  ↓
ITorrentInfoService.SearchForTorrentInfoFiltered(filter)
  ↓
PostgreSQL function: search_torrents_meta(...)
  ↓
Trigram similarity + metadata filters
  ↓
Scored + ordered results
  ↓
Map ImdbData to TorrentInfo
  ↓
Return TorrentInfo[]
```

### Ranking Logic

**Current Ranking:**
1. **Primary signal:** Trigram similarity score (`similarity(t."CleanedParsedTitle", query)`)
2. **Tiebreaker:** IngestedAt DESC (newer preferred)
3. **Threshold:** Configurable minimum score (default 0.85)

**Query Normalization:**
```csharp
// File: Parsing.CleanQuery()
1. Remove stopwords (a, the, and, of, in, on, with, to, for, by, is, it)
2. Collapse multiple spaces
3. Trim
```

**Pain Points:**
1. **No alias expansion:** "The Matrix" won't match if stored as just "Matrix"
2. **No anime normalization:** No handling for romanized/native titles, episode formats
3. **No noise stripping:** Release groups, codecs, fansubs not removed from queries
4. **Simple trigram only:** No semantic matching, no phonetic matching
5. **No ranking explainability:** Can't see why a result ranked high
6. **No query miss tracking:** Misses not logged for improvement

---

## 5. Current Caching Assumptions

### In-Memory Structures

1. **ParsedPages Dictionary:**
   ```csharp
   public ConcurrentDictionary<string, int> ExistingPages { get; private set; } = [];
   ```
   - Loaded entirely into memory at ingestion start
   - Never evicted during process lifetime
   - Problematic for large DMM archives

2. **Python RTN Results:**
   - Batch results held in memory during parsing
   - Batch size configurable (default 5000)
   - Can spike RAM during initial sync

3. **TorrentInfo Lists:**
   - Full torrent lists built in memory before bulk insert
   - Chunked for insertion but still retained

4. **Imdb Matching Data:**
   - `PopulateImdbData()` loads IMDB data for matching
   - Disposed after batch but still RAM-intensive

### Postgres-as-Cache

**Current Usage:**
- Torrents table acts as the "cache" of all known content
- ParsedPages tracks what's been ingested
- ImportMetadata stores last-import timestamps

**Missing:**
- No TTL-based invalidation
- No LRU eviction
- No freshness scoring
- No segment-level staleness

---

## 6. Current Query Normalization Logic

### Implemented Normalization

**File:** `/workspace/src/Zilean.Shared/Features/Utilities/Parsing.cs`

```csharp
public static string CleanQuery(string query)
{
    // 1. Remove stopwords via regex
    var cleanedQuery = StopWordRegex().Replace(query, "");
    // 2. Collapse multiple spaces
    cleanedQuery = SpaceRemovalRegex().Replace(cleanedQuery, " ").Trim();
    return cleanedQuery;
}
```

**Stopwords:** `a|the|and|of|in|on|with|to|for|by|is|it`

### RTN Parsing (Python)

**File:** `/workspace/src/Zilean.Shared/Features/Python/ParseTorrentNameService.cs`

Uses external `RTN` (Release Title Normalizer) Python library:
- Extracts: title, year, resolution, codec, group, etc.
- Produces: `parsed_title`, `normalized_title`, `cleaned_parsed_title`
- Handles: seasons, episodes, languages, HDR, audio, etc.

**Pain Points:**
1. **No anime-specific rules:** RTN may not handle anime titles well
2. **No alias database:** No expansion of alternate titles
3. **No phonetic normalization:** No handling for typos/variations
4. **No episode format unification:** S01E01 vs 1x01 vs EP.1 not unified
5. **No absolute episode numbering:** Anime absolute episodes not handled

---

## 7. Current Anime Handling Logic

### Current State

**Finding:** No explicit anime-specific handling found in codebase.

**What Exists:**
- General torrent parsing via RTN
- Season/episode extraction works for TV shows
- Category field can be "tvSeries"

**What's Missing:**
1. **No anime title aliases:** No mapping between romanized/native/English titles
2. **No fansub group stripping:** Groups like "HorribleSubs", "Erai-raws" not stripped
3. **No codec/source noise removal:** "H264", "AAC", "MKV" not stripped for search
4. **No absolute episode handling:** Anime often uses absolute ep numbering (ep.123)
5. **No OVA/special handling:** OVAs, ONAs, specials not differentiated
6. **No season pack detection:** "Complete Series" vs "Season 1" not distinguished
7. **No ranking boost for clean matches:** No preference for properly formatted anime titles

**Impact:** Anime search likely suffers from:
- False negatives due to title variations
- Noise in matching due to fansub tags
- Episode confusion (absolute vs season-based)

---

## 8. Current Logging and Diagnostics Behavior

### Logging Framework

**Framework:** Microsoft.Extensions.Logging (ILogger<T>)

**Current Logging:**
- Informational logs for sync start/complete
- Error logs for exceptions
- Some debug logs for batch processing

**Example:**
```csharp
_logger.LogInformation("Found {Count} files to parse", files.Count);
_logger.LogError(ex, "Error occurred during DMM Scraper Task");
```

### Diagnostic Endpoints

**File:** `/workspace/src/Zilean.ApiService/Features/HealthChecks/HealthCheckEndpoints.cs`

**Available:**
- `GET /healthchecks/ping` - Returns timestamp + "Pong!"

**Missing:**
- No freshness endpoint
- No ingestion status endpoint
- No queue status endpoint
- No search diagnostics
- No miss tracking dashboard

### Structured Logging

**Current State:**
- Logs are text-based, not structured JSON
- No correlation IDs
- No audit trail to disk
- No machine-readable format

**Pain Points:**
1. **Not machine-parseable:** AI agents can't easily analyze logs
2. **No persistence:** Logs lost on container restart
3. **No separation:** All logs mixed together
4. **No query audit:** Can't replay or analyze past searches
5. **No ranking debug:** Can't see why results ranked as they did

---

## 9. Current Healthcheck and Admin/Diagnostic Surfaces

### Exposed Endpoints

**Health:**
- `/healthchecks/ping` - Basic liveness check

**Search:**
- `POST /dmm/search` - Unfiltered search
- `GET /dmm/filtered` - Filtered search
- `GET /dmm/on-demand-scrape` - Trigger manual sync (auth required)

**Torznab:**
- Torznab-compatible indexer endpoints

**Torrents:**
- Optional cache-check endpoints (configurable)

**IMDB:**
- IMDB search endpoint

### Missing Diagnostics

1. **Freshness:**
   - When was last successful sync?
   - How many pages stale?
   - What's pending refresh?

2. **Ingestion:**
   - Current sync status
   - Historical sync stats
   - Failure counts by source

3. **Search:**
   - Recent queries
   - Miss rates
   - Average result counts
   - Score distributions

4. **Queue:**
   - Pending refresh jobs
   - Failed jobs
   - Queue depth

5. **System:**
   - RAM usage
   - DB size
   - Index sizes

---

## 10. Current Pain Points Creating RAM Pressure or Low Observability

### RAM Pressure Points

1. **Bulk Loading:**
   - `DmmFileEntryProcessor.ExistingPages` loads ALL parsed pages into ConcurrentDictionary
   - For large archives (10k+ pages), this is significant memory

2. **Batch Processing:**
   - Torrent batches held in memory during RTN parsing
   - Default batch size 5000, each TorrentInfo ~500 bytes = 2.5MB per batch
   - Multiple batches in flight during async processing

3. **Imdb Matching:**
   - `PopulateImdbData()` loads IMDB data into memory
   - Disposed after use but still peaks RAM

4. **Python Engine:**
   - Python.NET holds GIL and retains objects
   - GC not always prompt

5. **EF Core Tracking:**
   - DbContext tracks entities unless AsNoTracking() used
   - Can accumulate during long operations

### Observability Gaps

1. **No Query Audit Trail:**
   - Can't answer "what did users search for yesterday?"
   - Can't identify common miss patterns
   - Can't measure improvement over time

2. **No Ingestion Checkpoints:**
   - Can't resume mid-sync after crash
   - Must reprocess everything

3. **No Freshness Metrics:**
   - Can't tell which segments are stale
   - No proactive refresh triggers

4. **No Ranking Explainability:**
   - Can't debug why result X ranked above Y
   - No feature importance logging

5. **No Machine-Readable Logs:**
   - AI agents can't parse text logs easily
   - No structured event stream
   - No correlation IDs for tracing

---

## Code Map Summary

### Key Directories

```
/workspace
├── src/
│   ├── Zilean.ApiService/       # API endpoints, scheduling, health checks
│   │   └── Features/
│   │       ├── Search/          # Search endpoints
│   │       ├── Sync/            # Background sync jobs
│   │       ├── HealthChecks/    # Health endpoints
│   │       └── Bootstrapping/   # DI setup, scheduling config
│   │
│   ├── Zilean.Database/         # EF Core, migrations, DB services
│   │   ├── Functions/           # PostgreSQL functions
│   │   ├── Migrations/          # EF migrations
│   │   ├── Services/            # DB access layer
│   │   └── ZileanDbContext.cs   # EF model
│   │
│   ├── Zilean.Scraper/          # Ingestion logic
│   │   └── Features/
│   │       ├── Commands/        # CLI commands
│   │       ├── Ingestion/       # Scraping processors
│   │       └── Imdb/            # IMDB ingestion
│   │
│   └── Zilean.Shared/           # Shared models, config, utilities
│       └── Features/
│           ├── Configuration/   # Settings classes
│           ├── Dmm/             # TorrentInfo, ParsedPages models
│           ├── Python/          # RTN parsing service
│           └── Utilities/       # Parsing helpers
│
├── tests/
│   └── Zilean.Tests/            # xUnit tests
│
└── docs/
    └── Writerside/              # Documentation
```

### Critical Files for Modification

| File | Purpose | Change Priority |
|------|---------|-----------------|
| `ZileanDbContext.cs` | DB model | HIGH - Add new tables |
| `TorrentInfoService.cs` | Search logic | HIGH - Add telemetry, improve ranking |
| `DmmScraping.cs` | Ingestion orchestration | HIGH - Add checkpoints |
| `DmmFileEntryProcessor.cs` | Page processing | HIGH - Add segment state |
| `DmmService.cs` | DMM DB operations | HIGH - Add checkpoint ops |
| `Parsing.cs` | Query normalization | HIGH - Add anime normalization |
| `SearchEndpoints.cs` | Search API | MEDIUM - Add audit logging |
| `ZileanConfiguration.cs` | Config model | MEDIUM - Add new settings |
| `DmmConfiguration.cs` | DMM config | MEDIUM - Add freshness settings |
| `HealthCheckEndpoints.cs` | Diagnostics | MEDIUM - Add diagnostic endpoints |
| `SearchTorrentsMetaV5.cs` | DB search function | MEDIUM - Improve ranking |

---

## Proposed Architecture Design

### Core Principles

1. **Postgres-First:** Persist operational state, not just final results
2. **Checkpoint-Driven:** Every long-running operation checkpointed
3. **Stream-Oriented:** Process in bounded streams, not bulk loads
4. **Audit-Heavy:** Log everything structured and machine-readable
5. **Low-RAM-Safe:** Explicit bounds on all collections

### New Tables Required

```sql
-- 1. Source sync runs (audit trail for ingestion)
CREATE TABLE source_sync_runs (
    id BIGSERIAL PRIMARY KEY,
    source_type TEXT NOT NULL, -- 'dmm', 'zurg', 'zilean', 'generic'
    started_at TIMESTAMPTZ NOT NULL,
    ended_at TIMESTAMPTZ,
    status TEXT NOT NULL, -- 'running', 'completed', 'failed', 'partial'
    pages_processed INT DEFAULT 0,
    entries_processed INT DEFAULT 0,
    entries_inserted INT DEFAULT 0,
    entries_updated INT DEFAULT 0,
    retries INT DEFAULT 0,
    error_summary TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- 2. Source segments (page/file-level state tracking)
CREATE TABLE source_segments (
    id BIGSERIAL PRIMARY KEY,
    source_type TEXT NOT NULL,
    segment_key TEXT NOT NULL, -- filename, URL, etc.
    last_attempted_at TIMESTAMPTZ,
    last_successful_at TIMESTAMPTZ,
    checksum TEXT, -- for change detection
    status TEXT NOT NULL DEFAULT 'pending', -- 'pending', 'success', 'failed', 'stale'
    retry_count INT DEFAULT 0,
    stale_after TIMESTAMPTZ,
    error_summary TEXT,
    entry_count INT,
    UNIQUE(source_type, segment_key)
);
CREATE INDEX idx_source_segments_stale ON source_segments(stale_after) WHERE status != 'stale';
CREATE INDEX idx_source_segments_retry ON source_segments(retry_count, last_attempted_at) WHERE status = 'failed';

-- 3. Ingestion checkpoints (key-value checkpoints for resumability)
CREATE TABLE ingestion_checkpoints (
    id BIGSERIAL PRIMARY KEY,
    source_type TEXT NOT NULL,
    checkpoint_key TEXT NOT NULL,
    checkpoint_value JSONB NOT NULL,
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(source_type, checkpoint_key)
);

-- 4. Refresh jobs (queue for background refresh)
CREATE TABLE refresh_jobs (
    id BIGSERIAL PRIMARY KEY,
    trigger_type TEXT NOT NULL, -- 'scheduled', 'miss', 'manual', 'stale'
    query_fingerprint TEXT, -- for miss-triggered jobs
    target_scope JSONB, -- which segments/sources to refresh
    status TEXT NOT NULL DEFAULT 'pending', -- 'pending', 'running', 'completed', 'failed'
    dedupe_key TEXT, -- for deduplication
    scheduled_at TIMESTAMPTZ DEFAULT NOW(),
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    error_summary TEXT,
    UNIQUE(dedupe_key) WHERE status IN ('pending', 'running')
);
CREATE INDEX idx_refresh_jobs_pending ON refresh_jobs(scheduled_at) WHERE status = 'pending';

-- 5. Query audit (every search query logged)
CREATE TABLE query_audit (
    id BIGSERIAL PRIMARY KEY,
    raw_query TEXT NOT NULL,
    normalized_query TEXT,
    content_type TEXT, -- 'movie', 'tvSeries', 'anime', null
    parsed_season INT,
    parsed_episode INT,
    parsed_year INT,
    candidate_count INT,
    returned_count INT,
    top_score NUMERIC,
    result_info_hashes TEXT[], -- top N results
    triggered_refresh BOOLEAN DEFAULT FALSE,
    correlation_id UUID NOT NULL,
    elapsed_ms INT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_query_audit_created ON query_audit(created_at DESC);
CREATE INDEX idx_query_audit_normalized ON query_audit USING gin(to_tsvector('english', normalized_query));

-- 6. Query misses (queries with no/poor results)
CREATE TABLE query_misses (
    id BIGSERIAL PRIMARY KEY,
    query_fingerprint TEXT NOT NULL,
    raw_query_examples TEXT[] NOT NULL,
    content_hints JSONB, -- guessed type, season, etc.
    miss_count INT DEFAULT 1,
    last_seen_at TIMESTAMPTZ DEFAULT NOW(),
    refresh_job_id BIGINT REFERENCES refresh_jobs(id),
    refresh_status TEXT, -- null, 'pending', 'completed', 'failed'
    UNIQUE(query_fingerprint)
);
CREATE INDEX idx_query_misses_count ON query_misses(miss_count DESC);

-- 7. Title aliases (for search expansion)
CREATE TABLE title_aliases (
    id BIGSERIAL PRIMARY KEY,
    raw_title TEXT NOT NULL,
    canonical_title TEXT NOT NULL,
    alias_type TEXT, -- 'romanized', 'native', 'alternative', 'abbreviation'
    language TEXT, -- 'en', 'ja', 'zh', etc.
    script TEXT, -- 'latin', 'japanese', 'chinese'
    normalized_tokens TEXT[], -- pre-computed tokens
    created_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(raw_title, canonical_title)
);
CREATE INDEX idx_title_aliases_canonical ON title_aliases(canonical_title);
CREATE INDEX idx_title_aliases_tokens ON title_aliases USING gin(normalized_tokens);

-- 8. Search documents (denormalized search-optimized representation)
CREATE TABLE search_documents (
    id BIGSERIAL PRIMARY KEY,
    info_hash TEXT NOT NULL UNIQUE,
    title_primary TEXT NOT NULL,
    title_alternatives TEXT[],
    normalized_tokens TEXT[],
    title_vector tsvector,
    content_type TEXT,
    season_numbers INT[],
    episode_numbers INT[],
    absolute_episodes INT[],
    year INT,
    languages TEXT[],
    resolution TEXT,
    quality TEXT,
    release_group TEXT,
    source_type TEXT, -- 'bluray', 'web', 'dvd', etc.
    codec TEXT,
    audio TEXT[],
    adult BOOLEAN DEFAULT FALSE,
    category TEXT,
    imdb_id TEXT,
    freshness_score NUMERIC GENERATED ALWAYS AS (...) STORED,
    ingested_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_search_documents_vector ON search_documents USING gin(title_vector);
CREATE INDEX idx_search_documents_tokens ON search_documents USING gin(normalized_tokens);
CREATE INDEX idx_search_documents_freshness ON search_documents(freshness_score DESC);

-- 9. Ranking features (for explainability)
CREATE TABLE ranking_features (
    id BIGSERIAL PRIMARY KEY,
    query_audit_id BIGINT REFERENCES query_audit(id),
    info_hash TEXT NOT NULL,
    title_similarity NUMERIC,
    token_overlap INT,
    metadata_match_score NUMERIC,
    freshness_bonus NUMERIC,
    alias_match BOOLEAN,
    final_score NUMERIC,
    rank_position INT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
```

### New Configuration Options

```json
{
  "Zilean": {
    "AggressivePersistence": {
      "Enabled": true,
      "BatchSize": 1000,
      "IncrementalSyncIntervalMinutes": 15,
      "StaleSegmentTtlMinutes": 10080,
      "RefreshOnMissEnabled": true,
      "RefreshDedupeWindowMinutes": 60,
      "SearchAuditEnabled": true,
      "RankingAuditEnabled": true,
      "AuditDirectory": "/app/audit",
      "AuditJsonPretty": false,
      "MaxConcurrentRefreshJobs": 2,
      "AnimeNormalizationEnabled": true,
      "EnableStreamingIngestion": true,
      "CheckpointEveryNBatches": 10
    }
  }
}
```

### New Audit Log Files

Under configured `AuditDirectory`:

1. `ingestion-runs.jsonl` - Sync run lifecycle events
2. `ingestion-segments.jsonl` - Segment processing events
3. `search-queries.jsonl` - Every search query
4. `search-misses.jsonl` - Queries with poor/no results
5. `refresh-jobs.jsonl` - Refresh job lifecycle
6. `ranking-debug.jsonl` - Ranking breakdown for sampled queries
7. `failures.jsonl` - All errors/exceptions
8. `freshness.jsonl` - Freshness assessment events

**Format:** JSON Lines (one JSON object per line)

**Common Fields:**
```json
{
  "timestamp": "2025-04-25T12:34:56.789Z",
  "level": "Information|Warning|Error",
  "event_type": "ingestion.run.started",
  "correlation_id": "uuid-here",
  "subsystem": "ingestion|search|refresh",
  "source": "dmm|zurg|zilean",
  "status": "success|failure|partial",
  "elapsed_ms": 1234,
  "summary": "Human-readable summary",
  "details": {}
}
```

---

## Migration Impact Assessment

### Schema Changes

**Risk Level:** LOW (all additive)

1. **New tables:** No impact on existing queries
2. **New columns:** Not modifying existing tables initially
3. **New indexes:** May slow writes slightly, improve reads
4. **New functions:** Additive, don't replace existing

### Data Migration

**Required Migrations:**
1. Populate `source_segments` from existing `ParsedPages`
2. Initialize `ingestion_checkpoints` from `ImportMetadata`
3. Backfill `search_documents` from `Torrents` (can be done lazily)

**Migration Strategy:**
- Run migrations at application startup
- Backfill operations run asynchronously
- Old tables remain functional during transition

### Downtime

**Expected:** ZERO

- All migrations are additive
- Backfills run in background
- Old code paths remain functional

---

## Risk Assessment

### Technical Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| DB performance degradation | Medium | Medium | Careful indexing, batch sizes, VACUUM tuning |
| Increased disk I/O | High | Low | SSD recommended, async writes, batching |
| Migration failures | Low | High | Transactional migrations, rollback scripts |
| RAM pressure from new tables | Low | Medium | Streaming queries, bounded result sets |
| Python RTN compatibility | Low | Medium | Keep existing parser, add C# alternative |

### Operational Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Log volume explosion | Medium | Low | Rotation policy, sampling for verbose logs |
| Queue backlog growth | Medium | Medium | Max queue size, monitoring alerts |
| Stale segment accumulation | Low | Low | Automatic cleanup job, TTL |
| Refresh storm on startup | Medium | Medium | Randomized delays, rate limiting |

### Compatibility Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Breaking API changes | Low | High | Maintain backward compat, version endpoints |
| Config breaking changes | Low | Medium | Sensible defaults, deprecated warning period |
| Search behavior changes | Medium | Medium | Tunable thresholds, A/B testing capability |

---

## Implementation Phases Overview

### Phase 1: Audit and Code Map ✅
**Deliverable:** This document

### Phase 2: Postgres Persistence Foundation
**Duration:** 2-3 days
**Deliverables:**
- Migrations for new tables
- Entity classes for new tables
- Repository/Service interfaces
- Basic CRUD operations

### Phase 3: Incremental Ingestion and Stale-Segment Model
**Duration:** 3-4 days
**Deliverables:**
- Checkpoint-driven DMM sync
- Segment state tracking
- Staleness detection
- Resumable processing
- Bounded batch streaming

### Phase 4: Search-State Materialization and Ranking
**Duration:** 3-4 days
**Deliverables:**
- Search documents table
- Token tables
- Improved ranking function
- Score breakdown logging
- Alias expansion

### Phase 5: Anime-Specific Improvements
**Duration:** 2-3 days
**Deliverables:**
- Anime normalization pipeline
- Alias loading/expansion
- Episode format handling
- Noise token stripping
- Ranking adjustments

### Phase 6: File Auditing and Diagnostics
**Duration:** 2-3 days
**Deliverables:**
- JSONL audit logger
- Diagnostic endpoints
- Freshness dashboard data
- Queue inspection

### Phase 7: Benchmark and Documentation
**Duration:** 1-2 days
**Deliverables:**
- Benchmark suite
- Before/after comparison
- Deployment guide
- Troubleshooting guide

---

## Definition of Done (Per Phase)

Each phase is complete when:

1. ✅ Code changes merged to main branch
2. ✅ Migrations tested on fresh and existing DB
3. ✅ Unit tests passing (>80% coverage for new code)
4. ✅ Integration tests passing (Postgres-backed)
5. ✅ Documentation updated
6. ✅ Configuration options documented
7. ✅ Rollback procedure documented
8. ✅ Performance benchmarks collected
9. ✅ No regression in existing functionality
10. ✅ Code reviewed and approved

---

## Next Steps

1. **Review this audit** with stakeholders
2. **Prioritize phases** based on pain points
3. **Set up development environment** with test Postgres
4. **Begin Phase 2** implementation
5. **Iterate** based on testing feedback

---

## Appendix: Current Environment Variables

```bash
# Database
Zilean__Database__ConnectionString=Host=postgres;Database=zilean;...

# DMM
Zilean__Dmm__EnableScraping=true
Zilean__Dmm__ScrapeSchedule=0 * * * *
Zilean__Dmm__MinimumReDownloadIntervalMinutes=30
Zilean__Dmm__MaxFilteredResults=200
Zilean__Dmm__MinimumScoreMatch=0.85

# IMDB
Zilean__Imdb__EnableImportMatching=true
Zilean__Imdb__UseLucene=false

# Ingestion
Zilean__Ingestion__EnableScraping=false
Zilean__Ingestion__ScrapeSchedule=0 0 * * *

# Python
ZILEAN_PYTHON_VENV=/path/to/venv
ZILEAN_PYTHON_PYLIB=/path/to/python3.11.dll
```

## Appendix: Recommended New Environment Variables

```bash
# Aggressive Persistence
Zilean__AggressivePersistence__Enabled=true
Zilean__AggressivePersistence__BatchSize=1000
Zilean__AggressivePersistence__IncrementalSyncIntervalMinutes=15
Zilean__AggressivePersistence__StaleSegmentTtlMinutes=10080
Zilean__AggressivePersistence__RefreshOnMissEnabled=true
Zilean__AggressivePersistence__RefreshDedupeWindowMinutes=60
Zilean__AggressivePersistence__SearchAuditEnabled=true
Zilean__AggressivePersistence__RankingAuditEnabled=true
Zilean__AggressivePersistence__AuditDirectory=/app/audit
Zilean__AggressivePersistence__AuditJsonPretty=false
Zilean__AggressivePersistence__MaxConcurrentRefreshJobs=2
Zilean__AggressivePersistence__AnimeNormalizationEnabled=true
Zilean__AggressivePersistence__EnableStreamingIngestion=true
Zilean__AggressivePersistence__CheckpointEveryNBatches=10
```

---

**End of Audit Document**
