# Phase 2: Multi-Source Ingestion Pipeline

## TL;DR

> **Quick Summary**: Add unified torrent ingestion via Prowlarr Torznab API as a single `IInvocable` Coravel job (`ProwlarrSyncJob`). The job iterates configured indexers (e.g. Nyaa, TPB, LimeTorrents, SubsPlease), fetches via `IHttpClientFactory`, parses RSS 1.0 XML, and upserts to the `Torrents` table by `info_hash` with source tracking.
>
> **Deliverables**:
> - 1 adapter job class (`ProwlarrSyncJob`)
> - 1 config POCO (`ProwlarrConfiguration` with `Indexers[]` list)
> - 1 DB migration (`Source` column on `Torrents`, `TorrentSourceStats` table)
> - 1 bulk upsert extension (`ZileanDbContextExtensions`)
> - DI registration + scheduling wiring
> - 2 admin endpoints (`/admin/sources/status`, `/admin/sources/trigger/{name}`)
> - docker-compose.yaml update (1G memory + Prowlarr env vars)
> - 1 xUnit test class (`ProwlarrSyncJobTests`)
>
> **Estimated Effort**: Medium (8 implementation tasks, 4 review tasks)
> **Parallel Execution**: YES — 4 waves
> **Critical Path**: Wave 1 (foundation) → Wave 2 (job) → Wave 3 (integration) → Wave FINAL (review)

---

## Context

### Original Request
Add multi-source torrent ingestion to Zilean via Prowlarr's Torznab API. Prowlarr acts as a unified gateway to multiple indexers (Nyaa, TPB, LimeTorrents, SubsPlease, etc.). A single `ProwlarrSyncJob` iterates configured indexers, fetches latest torrents, parses XML, and upserts to the existing PostgreSQL `Torrents` table with source tracking.

**Dropped**: AnimeTosho (site down), BT4G (Cloudflare JS challenge not feasible with HttpClient), EZTV (site down — but can be re-enabled via Prowlarr if it comes back).

### Interview Summary
**Key Discussions**:
- Test Strategy: Tests-after (not TDD)
- Quick-and-dirty paradigm: Minimal abstractions, direct `IInvocable` job, no subprocess
- No Metis review (subagents broken); plan based on thorough direct research

**Sync Strategy**: Empty search (`q=`) on Torznab = "latest items" from each indexer. This is the equivalent of RSS feeds. Prowlarr unifies everything through a single Torznab contract.

**Contract Verification** (all confirmed live 2026-04-26):
- **Prowlarr Torznab**: `{BaseUrl}/{IndexerId}/api?t=search&apikey=KEY&q=&cat=2000,5000&extended=1` — empty `q=` returns freshest 100 results per indexer. Tested: `prowlarr.cloudpftc.com` with 8 indexers confirmed. XML: RSS 1.0, `infohash` from `<torznab:attr name="infohash">`
- **Coverage model**: DMM = breadth (historical catalog) + ProwlarrSyncJob = freshness (multiple indexers on cron)

### Existing Infrastructure (DO NOT recreate)
- **`IngestionCheckpoint`** entity and table already exist (`Id`, `Source`, `LastProcessed`[infohash], `Timestamp`, `Status`, `ItemsProcessed`). Used by DMM scraper. **Do NOT create a new checkpoint table**.
- **`IIngestionCheckpointService`** already registered in DI.
- **`TorznabConfiguration`** already exists (for the outgoing Torznab endpoint).
- Coravel scheduler already configured in `ServiceCollectionExtensions.cs`.

### Metis Review
**Skipped** — user confirmed subagents are broken. Self-review applied (see below).

---

## Work Objectives

### Core Objective
Add a single Prowlarr Torznab ingestion adapter (`ProwlarrSyncJob`) that iterates configured indexers, fetches torrent metadata, parses RSS 1.0 XML, and upserts to the existing `Torrents` table via `info_hash` with source priority handling.

### Concrete Deliverables
- `src/Zilean.ApiService/Features/Sync/ProwlarrSyncJob.cs` — Torznab API parser (RSS 1.0 XML)
- `src/Zilean.Shared/Features/Configuration/ProwlarrConfiguration.cs` — Config POCO with `Indexers[]` list
- `src/Zilean.Database/Migrations/*_AddSourceColumn.cs` — EF Core migration (Source column + TorrentSourceStats)
- `src/Zilean.Database/Extensions/ZileanDbContextExtensions.cs` — Bulk upsert extension
- `src/Zilean.ApiService/Endpoints/AdminEndpoints.cs` — Admin endpoints for source status / on-demand trigger
- `src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs` — DI + scheduling updates
- `tests/Zilean.Tests/Features/Sync/ProwlarrSyncJobTests.cs` — xUnit test class

### Definition of Done
- [ ] `curl http://localhost:8181/admin/sources/status` returns status for all configured indexers
- [ ] `curl -X POST http://localhost:8181/admin/sources/trigger/nyaa` triggers sync for the `nyaa` indexer
- [ ] Torrents table has rows with `source` column populated per indexer `SourceName`
- [ ] `/dmm/filtered` continues working unchanged
- [ ] Docker compose `memory: 1G` set on zilean service
- [ ] `ProwlarrSyncJobTests` passes with `dotnet test`

### Must Have
- Each indexer individually toggleable via `ProwlarrConfiguration.Indexers[i].Enabled`
- **Dedup strategy**: `TorrentSourceStats.LastSyncAt` checkpoint per indexer `SourceName` — stop paginating when item `pubDate` <= `LastSyncAt`, no duplicate processing
- **Freshness guarantees**: Paginate through all new items (offset 0→100→200...) until checkpoint overlap — never miss torrents published between cron runs
- `Source` column on `Torrents` table tracks which indexer (`SourceName`) inserted each row
- Deduplication by `info_hash` (INSERT ON CONFLICT)
- Source priority in upsert: `prowlarr=5, nyaa=4, yts=3, eztv=2, dmm=1` — only overwrite if new source priority >= existing
- `IHttpClientFactory` for all HTTP calls (User-Agent: "Zilean/2.0")
- Coravel `PreventOverlapping` per job
- Admin endpoints: `GET /admin/sources/status`, `POST /admin/sources/trigger/{sourceName}`

### Must NOT Have (Guardrails)
- Do NOT break `/dmm/filtered` or any existing DMM functionality
- Do NOT add Redis, RabbitMQ, MongoDB, or any new infrastructure
- Do NOT modify the Python scraper binary
- Do NOT change the Scraper project
- No new NuGet packages (HtmlAgilityPack dropped with BT4G)
- Do NOT create separate services layer — direct `IInvocable` job
- Do NOT over-engineer: no adapter interface, no base class, no factory
- Do NOT create a new checkpoint table — use existing `TorrentSourceStats.LastSyncAt`

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed. No exceptions.

### Test Decision
- **Infrastructure exists**: YES (xUnit + FluentAssertions + NSubstitute + Testcontainers)
- **Automated tests**: Tests-after (not TDD)
- **Framework**: xUnit
- **Pattern**: One test class for `ProwlarrSyncJob` in `tests/Zilean.Tests/`

### QA Policy
Every task MUST include agent-executed QA scenarios.
Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.{ext}`.

- **API/Backend**: Use `curl` — Send requests, assert status + response fields
- **Job execution**: Use `docker logs zilean` — Verify job output messages
- **Database**: Use `/diagnostics/stats` endpoint — Verify row counts change

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Start Immediately — foundation, ALL parallel):
├── Task 1: DB migration [quick]
├── Task 2: Prowlarr config [quick]
└── Task 3: Upsert helper [quick]

Wave 2 (After Wave 1 — unified job):
└── Task 4: ProwlarrSyncJob [unspecified-high]

Wave 3 (After Wave 2 — integration, ALL parallel):
├── Task 5: DI registration + scheduling [quick]
├── Task 6: Admin endpoints [quick]
└── Task 7: Docker updates [quick]

Wave 4 (After Wave 3):
└── Task 8: ProwlarrSyncJobTests [unspecified-low]

Wave FINAL (After ALL tasks — 4 parallel reviews):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Real QA execution (unspecified-high)
└── Task F4: Scope fidelity check (deep)
```

Critical Path: Task 1/2/3 → Task 4 → Task 5/6/7 → F1-F4
Parallel Speedup: Fast — single job, no inter-adapter dependency
Max Concurrent: 3 (Wave 1 and Wave 3)

### Agent Dispatch Summary
- Wave 1: 3 × `quick`
- Wave 2: 1 × `unspecified-high` — single unified adapter
- Wave 3: 3 × `quick`
- Wave 4: 1 × `unspecified-low`
- FINAL: oracle, unspecified-high, unspec-high, deep

### Dependency Matrix

| Task | Depends On | Blocks | Wave |
|------|-----------|--------|------|
| 1 | — | 4, 8 | 1 |
| 2 | — | 4 | 1 |
| 3 | — | 4 | 1 |
| 4 | 1, 2, 3 | 5-7 | 2 |
| 5 | 4 | 8 | 3 |
| 6 | 4 | — | 3 |
| 7 | — | — | 3 |
| 8 | 4, 5 | F1-F4 | 4 |
| F1 | 5-8 | — | FINAL |
| F2 | 5-8 | — | FINAL |
| F3 | 5-8 | — | FINAL |
| F4 | 5-8 | — | FINAL |

---

## TODOs

> Implementation + Test = ONE Task. Never separate.
> EVERY task MUST have: Recommended Agent Profile + Parallelization info + QA Scenarios.

- [x] 1. DB Migration — Add source column + stats table

  **What to do**:
  - Add `source` column (text, nullable) to `Torrents` table via EF Core migration
  - Create `TorrentSourceStats` table: `source` (text PK), `last_sync_at` (timestamptz), `torrent_count` (bigint), `last_error` (text nullable)
  - `TorrentSourceStats.LastSyncAt` serves as the per-indexer checkpoint: on sync, stop paginating when item `pubDate` <= this value
  - Generate EF Core migration: `dotnet ef migrations add AddSourceColumn`
  - Verify migration applies cleanly: `dotnet ef database update`

  **Existing — DO NOT recreate**:
  - `IngestionCheckpoints` table already exists (used by DMM scraper with `LastProcessed` infohash)
  - `imdb_id` column already exists on `Torrents`

  **Must NOT do**:
  - Do NOT drop or rename existing columns
  - Do NOT modify EF Core entity for `ImdbId` (already exists)
  - Do NOT create a new checkpoint table

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []
  - Reason: Simple EF Core schema change, no business logic

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3)
  - **Blocks**: Tasks 4-8, F1-F4
  - **Blocked By**: None

  **References**:
  - `src/Zilean.Shared/Features/Dmm/TorrentInfo.cs` — TorrentInfo entity (InfoHash PK, ImdbId exists, no Source yet)
  - `src/Zilean.Database/ZileanDbContext.cs` — DbContext with DbSets, OnModelCreating for configurations
  - `src/Zilean.Database/ModelConfiguration/TorrentInfoConfiguration.cs` — EF Core entity configuration pattern
  - `src/Zilean.Database/Migrations/` — Existing migration files for naming convention

  **Acceptance Criteria**:
  - [ ] `dotnet ef migrations add` succeeds with no errors
  - [ ] `dotnet ef database update` applies migration without data loss
  - [ ] `SELECT column_name FROM information_schema.columns WHERE table_name='Torrents' AND column_name='source'` returns 1 row
  - [ ] `TorrentSourceStats` table exists with correct schema

  **QA Scenarios**:

  ```
  Scenario: Migration applies cleanly
    Tool: Bash
    Preconditions: Docker PostgreSQL running
    Steps:
      1. cd src/Zilean.Database && dotnet ef migrations add AddSourceColumn
      2. dotnet ef database update
      3. docker exec -i zilean-db psql -U postgres -d zilean -c "SELECT column_name FROM information_schema.columns WHERE table_name='Torrents' AND column_name='source';"
    Expected Result: Migration created, applied. Query returns 'source' column.
    Evidence: .sisyphus/evidence/task-1-migration.sql
  ```

  **Commit**: YES
  - Message: `feat(db): add source column and stats table for multi-source ingestion`
  - Files: `src/Zilean.Database/Migrations/*`, `src/Zilean.Shared/Features/Dmm/TorrentInfo.cs`, `src/Zilean.Database/ModelConfiguration/*`
  - Pre-commit: `dotnet build`

- [x] 2. Config POCO — ProwlarrConfiguration with Indexers list

  **What to do**:
  - Create `src/Zilean.Shared/Features/Configuration/ProwlarrConfiguration.cs`:
    ```csharp
    public class ProwlarrConfiguration
    {
        public bool Enabled { get; set; } = false;
        public string BaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string Cron { get; set; } = "0 */6 * * *";
        public List<ProwlarrIndexer> Indexers { get; set; } = [];
    }

    public class ProwlarrIndexer
    {
        public int IndexerId { get; set; }
        public string SourceName { get; set; } = "";
        public string Categories { get; set; } = "2000,5000";
        public bool Enabled { get; set; } = false;
    }
    ```
  - Update `ZileanConfiguration.cs` to include:
    ```csharp
    public ProwlarrConfiguration Prowlarr { get; set; } = new();
    ```

  **Must NOT do**:
  - No complex validation logic — simple defaults only
  - No interface or base class for configs
  - Do NOT create NyaaConfiguration, EztvConfiguration, YtsConfiguration, or Bt4gConfiguration

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []
  - Reason: Simple POCO classes, no logic

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 3)
  - **Blocks**: Tasks 4-8
  - **Blocked By**: None

  **References**:
  - `src/Zilean.Shared/Features/Configuration/ZileanConfiguration.cs` — Root config class with nested POCOs
  - `src/Zilean.Shared/Features/Configuration/DmmConfiguration.cs` — Example: `EnableScraping:bool`, `ScrapeSchedule:string`

  **Acceptance Criteria**:
  - [ ] 2 files created/modified (1 config + 1 updated ZileanConfiguration)
  - [ ] `dotnet build` succeeds
  - [ ] Configuration binds from appsettings.json: `{ "Prowlarr": { "Enabled": true, "Indexers": [{ "IndexerId": 5, "SourceName": "nyaa" }] } }` reflects in config object

  **QA Scenarios**:

  ```
  Scenario: Config POCO binds correctly
    Tool: Bash (dotnet test)
    Preconditions: Test project builds
    Steps:
      1. Create test that loads appsettings with Prowlarr section
      2. Assert ProwlarrConfiguration.BaseUrl matches
      3. Assert default Cron = "0 */6 * * *"
      4. Assert Indexers list populated
    Expected Result: Config object populated from JSON
    Evidence: .sisyphus/evidence/task-2-config-bind.txt
  ```

  **Commit**: YES (with Task 3)
  - Message: `feat(config): add ProwlarrConfiguration with indexer list`

- [x] 3. Upsert helper — Torrent bulk upsert extension

  **What to do**:
  - Add extension method on `ZileanDbContext`:
    ```csharp
    public static async Task UpsertTorrentsAsync(
        this ZileanDbContext db,
        IEnumerable<TorrentInfo> torrents,
        string source,
        CancellationToken ct)
    ```
  - Uses raw SQL with `INSERT ... ON CONFLICT (info_hash) DO UPDATE`
  - Update: SET title, size, seeders, leechers, imdb_id, category, episode_info, languages, source, ingested_at = NOW()
  - Only update if current source priority <= new source priority (source priority: prowlarr=5, nyaa=4, yts=3, eztv=2, dmm=1)
  - Use EF Core's `Database.ExecuteSqlRawAsync` or `FromSqlRaw` for the upsert
  - Batch size: 1000 per chunk using `Enumerable.Chunk()`

  **Must NOT do**:
  - Do NOT add NuGet packages (use EF Core built-in raw SQL)
  - Do NOT use Dapper or any ORM besides EF Core
  - Do NOT drop and recreate — must be upsert

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []
  - Reason: Single extension method, straightforward SQL

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2)
  - **Blocks**: Tasks 4-8
  - **Blocked By**: None

  **References**:
  - `src/Zilean.Shared/Features/Dmm/TorrentInfo.cs` — All TorrentInfo properties to map in SQL
  - `src/Zilean.Database/ZileanDbContext.cs` — DbContext for extension method
  - PostgreSQL docs: `INSERT ... ON CONFLICT DO UPDATE` syntax

  **Acceptance Criteria**:
  - [ ] Method compiles and `dotnet build` succeeds
  - [ ] Insert new row: torrent not in DB → inserted
  - [ ] Update existing: same info_hash → updated
  - [ ] Priority check: lower-priority source does NOT overwrite higher-priority

  **QA Scenarios**:

  ```
  Scenario: Insert new torrent
    Tool: Bash (dotnet test)
    Preconditions: Testcontainers PostgreSQL running
    Steps:
      1. Create 1 TorrentInfo with new info_hash
      2. Call UpsertTorrentsAsync with source="nyaa"
      3. Query DB: SELECT * FROM "Torrents" WHERE "InfoHash" = '<hash>'
    Expected Result: 1 row returned, source = 'nyaa'
    Evidence: .sisyphus/evidence/task-3-upsert-insert.txt

  Scenario: Update existing torrent
    Tool: Bash (dotnet test)
    Preconditions: Torrent already inserted with source="dmm"
    Steps:
      1. Create same info_hash with updated title, source="prowlarr"
      2. Call UpsertTorrentsAsync
      3. Query DB for the row
    Expected Result: title updated to new value, source = 'prowlarr'
    Evidence: .sisyphus/evidence/task-3-upsert-update.txt
  ```

  **Commit**: YES (with Task 2)
  - Message: `feat(db): add upsert helper with source priority logic`

- [x] 4. ProwlarrSyncJob — Unified Torznab adapter

  **What to do**:
  - Create `src/Zilean.ApiService/Features/Sync/ProwlarrSyncJob.cs`
  - Implement `IInvocable, ICancellableInvocable` (Coravel)
  - Inject: `ILogger<ProwlarrSyncJob>`, `ZileanDbContext`, `IHttpClientFactory`, `ZileanConfiguration`, `IIngestionCheckpointService` (optional, for future use)
  - In `Invoke()`:
    1. Read `ProwlarrConfiguration` (BaseUrl, ApiKey, Indexers list)
    2. If `Enabled=false` or no enabled indexers, log and return
    3. For each indexer in `Indexers.Where(i => i.Enabled)`:
       a. Load `TorrentSourceStats` for this `SourceName` from DB (or create if absent)
       b. Read `LastSyncAt` as the checkpoint datetime
       c. Paginate: `GET {BaseUrl}/{IndexerId}/api?t=search&apikey={ApiKey}&q=&cat={Categories}&extended=1&offset={offset}`
       d. For each page: parse XML, extract items
       e. **Dedup check**: If page's first item `pubDate` <= `LastSyncAt`, STOP paginating (we've caught up)
       f. Otherwise: process items, call `offset += 100`, repeat
       g. After sync: update `TorrentSourceStats.LastSyncAt` to max(`pubDate` of all ingested items), update `TorrentCount`, clear `LastError`
       h. This ensures: no duplicate work (stops at checkpoint), no missed items (paginates until overlap)
       i. Extract from each `<item>`:
          - `<title>` → title
          - `<torznab:attr name="infohash" value="...">` → info_hash
          - `<size>` → size (integer bytes)
          - `<torznab:attr name="seeders" value="...">` → seeders
          - `<torznab:attr name="peers" value="...">` → leechers
          - `<guid>` → magnet link
          - `<pubDate>` → publish_date
          - `<category>` → category IDs
       j. Call `dbContext.UpsertTorrentsAsync(pageTorrents, indexer.SourceName, ct)`
       k. Log: `[ProwlarrSync] {SourceName} page {N}: upserted {count} torrents`
       l. Continue to next offset until checkpoint hit
    4. After all indexers processed, log summary: `[ProwlarrSync] Complete: {total} torrents from {N} indexers`
    5. On exception per indexer: log error, save to `TorrentSourceStats.LastError`, continue to next indexer

  **Default indexer configs** (user-configured, all verified live 2026-04-26):
  ```
  # Working indexers (confirmed returning results):
  IndexerId=5, SourceName="nyaa", Categories="2000,5000", Enabled=true   # 73 items
  IndexerId=2, SourceName="tpb", Categories="2000,5000", Enabled=true      # 33 items
  IndexerId=4, SourceName="limetorrents", Categories="2000,5000", Enabled=true  # 81 items
  IndexerId=6, SourceName="subsplease", Categories="5070,2000", Enabled=true   # 60 items (anime focus)
  # Non-working (disabled by default):
  # 1=1337x (disabled in Prowlarr), 3=EZTV (site down), 8=Magnetz (timeout), 9=TorrentDownloads (timeout)
  ```

  **Must NOT do**:
  - Do NOT log API key
  - Do NOT use `/api/v1/search` (that's internal REST JSON API)
  - Do NOT fetch all pages blindly — stop at checkpoint overlap
  - No parallel execution across indexers (respect rate limits, sequential OK)
  - Do NOT use `IngestionCheckpoint` for Prowlarr — use `TorrentSourceStats.LastSyncAt`

  **Verified**: Live at `prowlarr.cloudpftc.com`. 8 indexers confirmed. Torznab contract verified end-to-end.

  **Parallelization**: Wave 2, Blocks: Tasks 5-7, Blocked By: Tasks 1/2/3

  **References**:
  - `src/Zilean.ApiService/Features/Sync/DmmSyncJob.cs` — IInvocable pattern
  - Torznab XML: `<torznab:attr name="infohash">`, `<torznab:attr name="seeders">`, `<torznab:attr name="peers">`. Size in `<size>` (bytes).
  - Prowlarr endpoint: `{BaseUrl}/{IndexerId}/api?t=search&apikey=KEY&cat=2000,5000&extended=1`
  - Existing checkpoint service: `src/Zilean.ApiService/Features/Ingestion/IngestionCheckpointService.cs`

  **Acceptance Criteria**:
  - [ ] Job compiles and `dotnet build` succeeds
  - [ ] Paginates through multiple pages until checkpoint hit
  - [ ] Parses Torznab XML into TorrentInfo list correctly
  - [ ] With `Enabled=false` for all indexers, skips gracefully
  - [ ] `TorrentSourceStats.LastSyncAt` stored/updated correctly per indexer
  - [ ] On second run with same checkpoint, no duplicate items processed
  - [ ] Per-indexer errors logged and stored in `LastError`, do not crash other indexers

  **QA Scenarios**:
  ```
  Scenario: Job iterates enabled indexers with pagination
    Tool: Bash (dotnet test)
    Preconditions: Mock Torznab response with 250 items, checkpoint at +1h ago
    Steps:
      1. Inject 1 enabled indexer, mock 3 pages (100+100+50 items)
      2. Set checkpoint LastSyncAt to cover only first 2 pages
      3. Call Invoke()
      4. Assert 2 pages processed (200 items), 3rd page skipped (checkpoint overlap)
      5. Assert TorrentSourceStats.LastSyncAt updated to max pubDate of processed items
    Expected Result: Paginates correctly, stops at checkpoint, no duplicates
    Evidence: .sisyphus/evidence/task-4-prowlarr-sync.txt

  Scenario: Skipped disabled indexers
    Tool: Bash (dotnet test)
    Preconditions: 1 enabled, 2 disabled in config
    Steps: Call Invoke() — assert only 1 HTTP call
    Expected Result: Only enabled indexer queried
    Evidence: .sisyphus/evidence/task-4-skip-disabled.txt

  Scenario: Error isolation
    Tool: Bash (dotnet test)
    Preconditions: 2 enabled indexers, first throws HTTP 500
    Steps: Call Invoke()
    Expected Result: Second indexer still processed. First indexer's TorrentSourceStats.LastError populated.
    Evidence: .sisyphus/evidence/task-4-error-isolation.txt
  ```

  **Commit**: YES
  - Message: `feat(ingestion): add unified Prowlarr Torznab sync job`
  - Files: `src/Zilean.ApiService/Features/Sync/ProwlarrSyncJob.cs`

- [x] 5. DI Registration + Scheduling

  **What to do**:
  - In `src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs`:
    - Add `services.AddTransient<ProwlarrSyncJob>()` (always registered)
    - In `SetupScheduling()`:
      ```csharp
      if (configuration.Prowlarr.Enabled)
      {
          scheduler.Schedule<ProwlarrSyncJob>()
              .Cron(configuration.Prowlarr.Cron)
              .PreventOverlapping("ProwlarrSync");
      }
      ```
  - Ensure called from `Program.cs` builder chain (already handled by existing pattern)

  **Parallelization**: Wave 3, Blocks: Task 8, Blocked By: Task 4
  **QA Scenarios**: Verify DI resolves job, scheduler fires. Evidence: `task-5-di-schedule.txt`
  **Commit**: YES (with Tasks 6, 7)

- [x] 6. Admin Endpoints — Source status and trigger

  **What to do**:
  - Create `src/Zilean.ApiService/Endpoints/AdminEndpoints.cs`
  - `GET /admin/sources/status`: Return JSON array with all configured indexers from `ProwlarrConfiguration.Indexers`, enriched with `TorrentSourceStats` data:
    ```json
    [
      {
        "sourceName": "nyaa",
        "enabled": true,
        "indexerId": 5,
        "categories": "2000,5000",
        "lastSyncAt": "2026-04-26T12:00:00Z",
        "torrentCount": 15000,
        "cron": "0 */6 * * *",
        "lastError": null
      }
    ]
    ```
  - `POST /admin/sources/trigger/{sourceName}`: Resolve `ProwlarrSyncJob`, manually trigger sync for a specific indexer by `SourceName`. Optionally accept query param `?full=true` to reset `LastSyncAt` before triggering (full re-sync).
  - Register via existing `WebApplicationExtensions.cs` endpoint mapping chain

  **Parallelization**: Wave 3, Blocks: None, Blocked By: Task 4
  **QA Scenarios**: curl admin endpoints. Evidence: `task-6-admin.json`
  **Commit**: YES (with Tasks 5, 7)

- [x] 7. Docker Updates — Memory + Prowlarr env vars

  **What to do**:
  - `docker-compose.yaml`: `mem_limit: 1g` on zilean service, Prowlarr env vars:
    ```yaml
    - Zilean__Prowlarr__Enabled=true
    - Zilean__Prowlarr__BaseUrl=https://prowlarr.cloudpftc.com
    - Zilean__Prowlarr__ApiKey=
    - Zilean__Prowlarr__Cron=0 */6 * * *
    - Zilean__Prowlarr__Indexers__0__IndexerId=5
    - Zilean__Prowlarr__Indexers__0__SourceName=nyaa
    - Zilean__Prowlarr__Indexers__0__Enabled=true
    - Zilean__Prowlarr__Indexers__0__Categories=2000,5000
    - Zilean__Prowlarr__Indexers__1__IndexerId=2
    - Zilean__Prowlarr__Indexers__1__SourceName=tpb
    - Zilean__Prowlarr__Indexers__1__Enabled=true
    - Zilean__Prowlarr__Indexers__1__Categories=2000,5000
    - Zilean__Prowlarr__Indexers__2__IndexerId=4
    - Zilean__Prowlarr__Indexers__2__SourceName=limetorrents
    - Zilean__Prowlarr__Indexers__2__Enabled=true
    - Zilean__Prowlarr__Indexers__2__Categories=2000,5000
    - Zilean__Prowlarr__Indexers__3__IndexerId=6
    - Zilean__Prowlarr__Indexers__3__SourceName=subsplease
    - Zilean__Prowlarr__Indexers__3__Enabled=true
    - Zilean__Prowlarr__Indexers__3__Categories=5070,2000
    ```
  - `.env.example`: Add Prowlarr vars with empty defaults

  **Parallelization**: Wave 3, Blocks: None, Blocked By: None
  **QA Scenarios**: `docker compose config | grep mem_limit` and `docker compose config | grep Zilean__Prowlarr`. Evidence: `task-7-docker.txt`
  **Commit**: YES

- [x] 8. ProwlarrSyncJobTests — xUnit test class

  **What to do**:
  - Create `tests/Zilean.Tests/Features/Sync/ProwlarrSyncJobTests.cs`
  - Test scenarios:
    1. Mocked Torznab XML parsing — verify correct TorrentInfo fields extracted
    2. Disabled indexer skip — verify no HTTP call for disabled indexer
    3. Empty results — graceful handling of zero items
    4. HTTP error handling — per-indexer error isolation
    5. Checkpoint pagination — stops at `LastSyncAt`, updates checkpoint correctly
    6. Source column populated with correct `SourceName`
    7. Rate limiting / sequential execution — indexers processed one at a time
  - Use `IClassFixture<TestDbFixture>` with Testcontainers for DB assertions
  - Mock `IHttpClientFactory` with `HttpMessageHandler` mock for HTTP responses

  **Parallelization**: Wave 4, Blocks: F1-F4, Blocked By: Tasks 4, 5
  **QA Scenarios**: `dotnet test --filter ProwlarrSyncJob`
  **Evidence**: `task-8-tests.txt`
  **Commit**: `test: add ProwlarrSyncJob unit tests`

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`: Verify ProwlarrSyncJob built, config binds, endpoints respond, docker memory 1G, no broken DMM endpoints. VERDICT.
- [ ] F2. **Code Quality Review** — `unspecified-high`: `dotnet build` + `dotnet test`. Check code quality. VERDICT.
- [ ] F3. **Real QA Execution** — `unspecified-high`: Run all curl-based QA scenarios. Evidence to `.sisyphus/evidence/final-qa/`. VERDICT.
- [ ] F4. **Scope Fidelity Check** — `deep`: Verify exactly 1 unified job built, no extra code, no DMM breakage. VERDICT.

---

## Commit Strategy

All tasks committed individually. Squash on merge to main.

---

## Success Criteria

### Verification Commands
```bash
curl http://localhost:8181/admin/sources/status | jq '.sources | length'  # Expected: number of configured indexers
curl -X POST http://localhost:8181/admin/sources/trigger/nyaa              # Expected: {"triggered":"nyaa"}
curl http://localhost:8181/dmm/filtered?query=test                         # Expected: works unchanged
dotnet test                                                                 # Expected: all pass
docker compose config | grep mem_limit                                     # Expected: 1g
```

### Final Checklist
- [ ] ProwlarrSyncJob builds and runs
- [ ] `/admin/sources/status` returns indexer info
- [ ] `/admin/sources/trigger/{sourceName}` triggers on-demand sync for specific indexer
- [ ] `/dmm/filtered` continues working
- [ ] Docker memory limit 1G
- [ ] All tests pass
- [ ] Source column populated with correct indexer SourceNames
- [ ] TorrentSourceStats tracks LastSyncAt per indexer
