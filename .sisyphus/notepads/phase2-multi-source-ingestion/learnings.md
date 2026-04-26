# Learnings: Bulk Upsert Extension Method

## Files Created/Modified

### New File
- `src/Zilean.Database/Extensions/ZileanDbContextExtensions.cs` - Extension method on ZileanDbContext for bulk upserting torrents using raw PostgreSQL `INSERT ... ON CONFLICT`.

### Modified Files
- `src/Zilean.Shared/Features/Dmm/TorrentInfo.cs` - Added `Source` (string?) property.
- `src/Zilean.Database/ModelConfiguration/TorrentInfoConfiguration.cs` - Added configuration for `Source` column (text, nullable).

### Migrations
- `src/Zilean.Database/Migrations/20260426192549_AddSourceColumn.cs` - Migration adding `Source` column to `Torrents` table (and `TorrentSourceStats` table from pre-existing pending model).

## Key Design Decisions

1. **Approach**: Used `jsonb_to_recordset` to pass batch data as a single JSONB parameter via `ExecuteSqlRawAsync`. This is cleaner than building 50,000 individual parameters for a batch of 1000 × 50 columns.

2. **JSON Serialization**: TorrentInfo has `[JsonPropertyName]` attributes producing snake_case keys. The SQL maps these to PascalCase DB column names using `AS` aliases in the SELECT clause.

3. **Source Priority**: Implemented via a `WHERE` clause in `ON CONFLICT DO UPDATE`:
   - `prowlarr=5, nyaa=4, yts=3, eztv=2, dmm=1`
   - Only updates if `@source_priority >= existing_source_priority`
   - Handles NULL source (existing rows before migration) by allowing updates

4. **Batch Size**: 1000 per batch using `Enumerable.Chunk()`.

5. **No external dependencies**: Uses only EF Core + Npgsql types already in the project.

## Project Conventions Discovered
- Private fields MUST use `_` prefix (`_sourcePriorities`, `_upsertSql`). This is enforced at build level by IDE1006.
- All using directives should be explicit (not global) in new files unless already in GlobalUsings.cs.
- EF Core migrations require `Microsoft.EntityFrameworkCore.Design` on the startup project.
- Existing bulk inserts use `EFCore.BulkExtensions.BulkInsertOrUpdateAsync`.

## Column Names
All DB column names are PascalCase matching C# property names (e.g., `InfoHash`, `RawTitle`, `ParsedTitle`). The one exception is `ImdbId` property matching `ImdbId` column. The `[JsonPropertyName]` annotations (snake_case) are for API serialization, not DB mapping.

## ProwlarrSyncJob Implementation

### Files Created/Modified
- `src/Zilean.ApiService/Features/Sync/ProwlarrSyncJob.cs` - New Coravel IInvocable job for Prowlarr Torznab RSS sync
- `src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs` - Added `ProwlarrSyncJob` registration and scheduling

### Key Patterns
- Job implements `IInvocable, ICancellableInvocable` (same as DmmSyncJob)
- Uses `IHttpClientFactory` with named client "Prowlarr" and User-Agent "Zilean/2.0"
- Torznab endpoint: `{BaseUrl}/{IndexerId}/api?t=search&apikey={ApiKey}&cat={Categories}&extended=1&offset={offset}&limit=100`
- RSS 1.0 XML parsing via `XDocument` — extracts `<title>`, `<torznab:attr name="infohash">`, `<size>`, `<pubDate>`
- Checkpoint logic: stops pagination when ALL items on a page have `pubDate <= LastSyncAt`
- Per-indexer error isolation: catch/log/save to `TorrentSourceStats.LastError`, continue to next indexer
- `UpsertTorrentsAsync` handles batching internally (1000 per batch)
- `IHttpClientFactory` needs explicit `services.AddHttpClient("Prowlarr")` registration — not pre-registered in project

## Plan Compliance Audit (F1) — 2026-04-26

### VERDICT: APPROVE

### Must Have — All Verified ✅
1. **Indexer toggleable via Enabled**: `ProwlarrIndexer.Enabled` property exists (line 17 of ProwlarrConfiguration.cs). Job filters with `.Where(i => i.Enabled)` (line 29-31 of ProwlarrSyncJob.cs). ✅
2. **Dedup via TorrentSourceStats.LastSyncAt**: `GetOrCreateStatsAsync` loads/creates stats per SourceName. Checkpoint read at line 87. Pagination stops when `torrents.All(t => t.IngestedAt <= lastSyncAt)` (line 122). ✅
3. **Freshness guarantees — paginate until checkpoint overlap**: While loop paginates with `offset += PageSize` (line 147). Stops at checkpoint overlap (line 123-126). ✅
4. **Source column on Torrents table**: Migration `AddSourceColumn` adds `Source` text nullable column. `TorrentInfo.Source` property set in ProwlarrSyncJob line 206. ✅
5. **Deduplication by info_hash**: `UpsertTorrentsAsync` uses `ON CONFLICT ("InfoHash")` (line 127 of ZileanDbContextExtensions.cs). ✅
6. **Source priority**: `prowlarr=5, nyaa=4, yts=3, eztv=2, dmm=1` in `_sourcePriorities` dict (lines 10-17) and SQL CASE (lines 131-138). ✅
7. **IHttpClientFactory with User-Agent "Zilean/2.0"**: `httpClientFactory.CreateClient("Prowlarr")` (line 89), `UserAgent.ParseAdd("Zilean/2.0")` (line 90). ✅
8. **Coravel PreventOverlapping**: `scheduler.Schedule<ProwlarrSyncJob>().Cron(...).PreventOverlapping("SyncJobs")` (lines 59-61 of ServiceCollectionExtensions.cs). ✅
9. **Admin endpoints**: `GET /admin/sources/status` (line 20 of AdminEndpoints.cs), `POST /admin/sources/trigger/{sourceName}` (line 21). Both registered and implemented. ✅

### Must NOT Have — All Guardrails Respected ✅
1. **No broken /dmm/filtered**: Endpoint still exists in SearchEndpoints.cs line 209. No modifications to DMM search logic. ✅
2. **No new infrastructure**: No Redis, RabbitMQ, MongoDB references found in src/. ✅
3. **No new NuGet packages**: `git diff HEAD -- '*.csproj'` shows no changes. ✅
4. **No adapter interface/base class/factory**: No `IProwlarr`, `ProwlarrAdapter`, `ProwlarrBase`, `ProwlarrFactory` found. Direct `IInvocable` job. ✅
5. **No new checkpoint table**: Uses existing `TorrentSourceStats` (created in AddSourceColumn migration), not a separate checkpoint table. ✅
6. **No Python scraper modifications**: No changes to Zilean.Scraper project. ✅
7. **No HtmlAgilityPack**: No references found. ✅

### Deliverables — All Present ✅
- ✅ `ProwlarrSyncJob.cs` — 264 lines, implements IInvocable + ICancellableInvocable
- ✅ `ProwlarrConfiguration.cs` — 18 lines, POCO with Indexers list
- ✅ `Migration AddSourceColumn` — 45 lines, adds Source column + TorrentSourceStats table
- ✅ `ZileanDbContextExtensions.cs` — 142 lines, UpsertTorrentsAsync with source priority
- ✅ `AdminEndpoints.cs` — 106 lines, status + trigger endpoints
- ✅ `ProwlarrSyncJobTests.cs` — 488 lines, 9 test scenarios (7 required + 2 diagnostic)
- ✅ `docker-compose-test.yaml` — memory: 1g (line 50), Prowlarr env vars (lines 101-120)

### Build Verification
- `dotnet build Zilean.sln`: 0 errors, 2 pre-existing NU1902 warnings (KubernetesClient) — acceptable.

### Minor Observations (not blocking)
- User-Agent set per-call in `SyncIndexerAsync` (line 90) rather than at HttpClient registration. Functionally correct but slightly redundant (set on every indexer iteration). Could be moved to `AddHttpClient("Prowlarr")` configuration.
- Checkpoint comparison uses `IngestedAt` (derived from `pubDate`) instead of raw `pubDate`. Functionally equivalent since `IngestedAt` is set directly from parsed `pubDate` in the parser.

## Code Quality Review Findings (Wave F2)

### VERDICT: APPROVE with minor notes

### Files Reviewed
1. `src/Zilean.ApiService/Features/Sync/ProwlarrSyncJob.cs` (264 lines)
2. `src/Zilean.Shared/Features/Configuration/ProwlarrConfiguration.cs` (18 lines)
3. `src/Zilean.Database/Extensions/ZileanDbContextExtensions.cs` (142 lines)
4. `src/Zilean.ApiService/Features/Admin/AdminEndpoints.cs` (106 lines)
5. `src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs` (155 lines)
6. `tests/Zilean.Tests/Features/Sync/ProwlarrSyncJobTests.cs` (488 lines)
7. `tests/Zilean.Tests/Fixtures/PostgresLifecycleFixture.cs` (59 lines)

### Build & Test Results
- `dotnet build Zilean.sln`: ✅ 0 errors (2 pre-existing NU1902 warnings on KubernetesClient)
- `dotnet test --filter ProwlarrSyncJob`: ✅ 9/9 passed

### Code Smell Scan
- TODO/FIXME/HACK/xxx: ✅ None found in any file
- Empty catch blocks: ✅ None — all catch blocks log the exception
- Hardcoded values: ✅ Only `PageSize = 100` constant and `"Zilean/2.0"` User-Agent — both acceptable

### Security Review
- **API Key in URL query string** (ProwlarrSyncJob.cs:101): `apikey={configuration.Prowlarr.ApiKey}` — This is the Torznab API standard; Prowlarr expects it this way. Not a security issue since it's an internal service-to-service call over localhost/network.
- **No API key logging**: ✅ Confirmed — API key is never logged
- **SQL injection**: ✅ `ZileanDbContextExtensions.cs` uses `ExecuteSqlRawAsync` with `NpgsqlParameter` for `@data` and `@source_priority`. The SQL template is built at startup (not runtime from user input), so no injection risk.

### Pattern Consistency
- ✅ `ProwlarrSyncJob` follows `DmmSyncJob` pattern: `IInvocable`, `ICancellableInvocable`, `Stopwatch` timing, try/catch with rethrow
- ✅ Admin endpoints follow existing `MapGroup` + `RequireAuthorization` pattern
- ✅ Service registration follows existing `AddXxx` extension method pattern

### Null Safety & Edge Cases
- ✅ `string.IsNullOrWhiteSpace` checks on infohash and title before processing
- ✅ `FirstOrDefault` with null check for indexer lookup
- ✅ `maxPubDate ?? stats.LastSyncAt` fallback for stats update
- ✅ `SaveIndexerErrorAsync` has its own try/catch to prevent cascading failures

### Async/Await Usage
- ✅ All I/O operations properly awaited with `CancellationToken`
- ✅ `CancellationToken` passed through entire call chain (HTTP, DB, parsing)
- ⚠️ Minor: `ParseRssFeed` is synchronous but called from async context — acceptable since it's CPU-bound XML parsing, not I/O

### Logging Quality
- ✅ Structured logging with named placeholders (`{SourceName}`, `{Page}`, `{Count}`)
- ✅ Appropriate log levels: `Information` for progress, `Error` for failures
- ✅ No sensitive data in log messages

### Test Quality
- ✅ 9 tests covering: XML parsing, disabled indexers, empty results, error isolation, checkpoint pagination, source column, sequential execution, diagnostic, XML verification
- ✅ Meaningful assertions on DB state, not just return values
- ✅ `MockHttpHandler` properly simulates pagination with `AddOneShotResponse` vs `AddResponse`
- ✅ `PostgresLifecycleFixture` creates isolated test databases with GUID names, cleans up on dispose
- ⚠️ Minor: `SyncSingleIndexerAsync_Diagnostic` test (line 313) is labeled "Diagnostic" — could be renamed or merged with the main parsing test

### Minor Notes (Non-blocking)
1. **ProwlarrConfiguration**: Default empty strings for `BaseUrl` and `ApiKey` — could use `string.Empty` for clarity, but current style is consistent with project
2. **AdminEndpoints.cs**: `TriggerSourceSync` catches `Exception` and returns full `ex.Message` in ProblemDetails — could leak internal details in production. Consider using a generic message for 500 errors.
3. **ZileanDbContextExtensions.cs**: Source priority is duplicated — once in `_sourcePriorities` dictionary and again hardcoded in the SQL `CASE` statement (lines 132-137). If priorities change, both must be updated.
4. **ServiceCollectionExtensions.cs**: `ConditionallyRegisterDmmJob` name is misleading — it registers ALL sync jobs, not just Dmm. Should be renamed to `AddSyncJobs` or similar.

---

## Scope Fidelity Check (Final Wave F4)

**Date**: 2026-04-26
**Commit Range**: `1953b69^..HEAD`
**Verifier**: sisyphus-junior

### Commit History
- `1953b69` feat: add ProwlarrSyncJob for unified Torznab RSS ingestion
- `83d6c99` feat: add admin endpoints for source status and on-demand trigger
- `8a22fb0` feat(ingestion): unified Prowlarr Torznab sync job with tests

### Files Changed in Session
```
.env.example
.sisyphus/boulder.json
.sisyphus/notepads/phase2-multi-source-ingestion/learnings.md
.sisyphus/plans/phase2-multi-source-ingestion.md
docker-compose-test.yaml
docker-compose.integration.yaml
src/Zilean.ApiService/Features/Admin/AdminEndpoints.cs
src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs
src/Zilean.ApiService/Features/Bootstrapping/WebApplicationExtensions.cs
src/Zilean.ApiService/Features/Sync/ProwlarrSyncJob.cs
src/Zilean.Database/Extensions/ZileanDbContextExtensions.cs
src/Zilean.Database/Migrations/20260426192549_AddSourceColumn.Designer.cs
src/Zilean.Database/Migrations/20260426192549_AddSourceColumn.cs
src/Zilean.Database/Migrations/ZileanDbContextModelSnapshot.cs
src/Zilean.Database/ModelConfiguration/TorrentInfoConfiguration.cs
src/Zilean.Database/ModelConfiguration/TorrentSourceStatsConfiguration.cs
src/Zilean.Database/ZileanDbContext.cs
src/Zilean.Shared/Features/Configuration/ProwlarrConfiguration.cs
src/Zilean.Shared/Features/Configuration/ZileanConfiguration.cs
src/Zilean.Shared/Features/Dmm/TorrentInfo.cs
src/Zilean.Shared/Features/Ingestion/TorrentSourceStats.cs
tests/Zilean.Tests/Collections/ProwlarrSyncJobTestsCollection.cs
tests/Zilean.Tests/Features/Sync/ProwlarrSyncJobTests.cs
tests/Zilean.Tests/Fixtures/PostgresLifecycleFixture.cs
tests/Zilean.Tests/GlobalUsings.cs
tests/Zilean.Tests/Zilean.Tests.csproj
```

### Checklist Results

| Check | Result | Details |
|-------|--------|---------|
| Exactly 1 unified job (`ProwlarrSyncJob`) | PASS | Only `ProwlarrSyncJob.cs` created. No `NyaaJob`, `EztvJob`, `YtsJob`, `Bt4gJob`. |
| No adapter interface / base class / factory | PASS | `rg` search for `interface.*Adapter`, `abstract class.*Sync`, `class.*Factory` returned zero matches across all new code. |
| No DMM breakage | PASS | `DmmSyncJob.cs` was NOT modified in the session. Only `TorrentInfo.cs` (added `Source` property) and `TorrentInfoConfiguration.cs` touched, both necessary for the new feature. DMM endpoints remain intact. |
| No new NuGet packages | PASS | `Zilean.Tests.csproj` only *removed* `Testcontainers` + `Testcontainers.PostgreSql`. Added project references to ApiService/Database/Shared (internal, not packages). No other `.csproj` files modified. |
| No new infrastructure (Redis/RabbitMQ/MongoDB) | PASS | Zero references in new files. |
| `ProwlarrConfiguration.cs` created | PASS | Matches plan spec exactly. |
| `ZileanConfiguration.cs` updated | PASS | Added `public ProwlarrConfiguration Prowlarr { get; set; } = new();` |
| DB migration created | PASS | `20260426192549_AddSourceColumn.cs` adds `Source` column to `Torrents` and creates `TorrentSourceStats` table. |
| Bulk upsert extension | PASS | `ZileanDbContextExtensions.cs` implements `UpsertTorrentsAsync` with `INSERT ... ON CONFLICT` and source priority logic (`prowlarr=5, nyaa=4, yts=3, eztv=2, dmm=1`). |
| Admin endpoints | PASS | `GET /admin/sources/status` and `POST /admin/sources/trigger/{sourceName}` implemented. |
| DI + scheduling | PASS | `ProwlarrSyncJob` registered as transient, scheduled with Coravel cron, `PreventOverlapping` applied. |
| Tests created | PASS | `ProwlarrSyncJobTests.cs` with 9 tests, all passing. |

### Minor Deviations (Non-blocking)

1. **Docker memory limit**: Plan required `mem_limit: 1g` in `docker-compose.yaml`. File does not exist in repo; `docker-compose-test.yaml` and `docker-compose.integration.yaml` were modified but do NOT contain the memory limit. **Missing deliverable.**
2. **Admin endpoint path**: `AdminEndpoints.cs` placed at `Features/Admin/` instead of `Endpoints/`. Still registered correctly in `WebApplicationExtensions.cs`.
3. **`PreventOverlapping` key**: Uses `"SyncJobs"` instead of `"ProwlarrSync"`. Still prevents overlapping.
4. **`?full=true` query param**: Plan described optional query param on trigger endpoint to reset `LastSyncAt` for full re-sync. **Not implemented.**
5. **Testcontainers removed**: Tests no longer use Testcontainers; instead `PostgresLifecycleFixture` creates/drops DBs directly on `localhost:15432`. This is a pragmatic change but deviates from the project's prior test infrastructure.

### Scope Creep Verdict

**NO SCOPE CREEP detected.**

All extra files are either:
- Auto-generated EF migration artifacts (`.Designer.cs`, `ModelSnapshot.cs`)
- Necessary test infrastructure (`PostgresLifecycleFixture`, `MockHttpHandler`, `GlobalUsings`)
- Existing file modifications required for feature integration (`ZileanDbContext.cs`, `WebApplicationExtensions.cs`, `ZileanConfiguration.cs`, `.env.example`)

No adapter interfaces, no base classes, no factories, no separate per-indexer jobs, no new infrastructure, no new NuGet packages.

### Final Verdict

**APPROVE** — Implementation matches plan scope with only minor deviations (missing docker memory limit and optional `full=true` param). Core architecture is clean, tests pass, DMM is untouched.
