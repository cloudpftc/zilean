# Fix All Remaining Zilean Issues: Diagnostics, Scraper, Indexes, Tests

## TL;DR

> **Quick Summary**: Fix 4 remaining issues: broken `/diagnostics/stats` endpoint (EF Core SqlQuery JOIN defect), DMM scraper not persisting torrents (silent exception swallowing), missing PostgreSQL indexes (4 GIN indexes), and ensure all tests pass.

> **Deliverables**:
> - `/diagnostics/stats` returns 200 with table stats, row counts, total DB size
> - DMM scraper successfully ingests torrents (Torrents table > 0 rows)
> - `/healthchecks/health` reports `indexes.isHealthy: true`
> - `dotnet test` exits 0 with all tests passing

> **Estimated Effort**: Short (4 parallel tasks in Wave 1, 2 sequential in Wave 2, 1 in Wave 3)
> **Parallel Execution**: YES - Waves 1 and 3 are parallel
> **Critical Path**: Task 1 → Task 2 → Task 4

---

## Context

### Original Request
Fix everything broken so the full test suite passes and the DMM scraper ingests torrents into the database.

### Interview Summary
**Key Discussions** (from prior session):
- Dockerfile restored to upstream iPromKnight/zilean pattern (multi-stage build)
- Logging configured: Serilog at Debug level, Microsoft/EF Core noise suppressed to Warning
- Database fixes applied: temp table (ON COMMIT DROP → IF NOT EXISTS + TRUNCATE), match_torrents_to_imdb return type (DOUBLE PRECISION → REAL), C# Score type (double → float)
- DmmFileDownloader.cs: fixed missing temp directory handling on fresh container

**Research Findings**:
- `/diagnostics/stats` broken by EF Core `SqlQuery<T>` inability to handle JOINs with table aliases (column mapping creates shadow property `s.Value`)
- DMM scraper processes files, parses titles, reaches IMDb matching ("Starting PostgreSQL trigram matching for 5000 torrents"), then goes silent — result is `Torrents = 0`
- `GenericProcessor.OnProcessTorrentsAsync` catches ALL exceptions and logs only `LogWarning` — errors are silently swallowed
- Missing indexes: `idx_cleaned_parsed_title_trgm`, `idx_seasons_gin`, `idx_episodes_gin`, `idx_languages_gin` defined in config but never migrated

### Metis Review
**Identified Gaps** (addressed):
- `/diagnostics/stats` root cause: EF Core `SqlQuery<T>` + JOIN = entity mapping failure → Fix: raw NpgsqlConnection + Dapper (same pattern as `ImdbPostgresMatchingService.cs`)
- DMM scraper silent failure: matched torrents get their `ImdbId` set (objects modified in-place), but exception in `BulkInsertOrUpdateAsync` is caught and logged as Warning → Fix: add Debug logging around critical calls, escalate exception logging
- Missing indexes: defined in `TorrentInfoConfiguration.cs` lines 248-263 but not in any migration → Fix: create new EF Core migration
- Guardrail: Do NOT modify `match_torrents_to_imdb` function (already fixed), do NOT refactor `GenericProcessor` beyond adding logs, do NOT touch scraper binary/Python parser

---

## Work Objectives

### Core Objective
Fix 4 remaining issues so Zilean is fully functional: diagnostic endpoints work, scraper ingests data, indexes exist, and all tests pass.

### Concrete Deliverables
- `/diagnostics/stats` returns HTTP 200 with table stats JSON (row counts, sizes, total DB size, last ingestion time)
- `/healthchecks/health` reports `indexes.isHealthy: true` (all 4 GIN indexes present)
- `Torrents` table has > 0 rows after scraper run
- `dotnet test` exits 0 with 0 failed, 0 skipped

### Definition of Done
- [ ] `curl -s http://localhost:8181/diagnostics/stats | jq '.tables | length'` > 0
- [ ] `curl -s http://localhost:8181/diagnostics/stats | jq '.totalDatabaseSizeMb'` is a number > 0
- [ ] `curl -s http://localhost:8181/healthchecks/health | jq '.indexes.isHealthy'` == `true`
- [ ] `curl -s http://localhost:8181/diagnostics/freshness | jq '.totalTorrents'` > 0
- [ ] `dotnet test --logger "console;verbosity=detailed"` exit code 0
- [ ] **36-test integration plan** (`docs/test-report-2026-04-25.md`) passes 36/36

### Must Have
- `/diagnostics/stats` working with all table stats
- DMM scraper ingesting torrents into database
- All 4 GIN indexes created
- All existing tests passing

### Must NOT Have (Guardrails)
- Do NOT modify `match_torrents_to_imdb` function (already fixed this session)
- Do NOT refactor `GenericProcessor` error handling beyond adding log statements
- Do NOT change scraper binary or Python RTN parser
- Do NOT add new features or schema changes beyond the 4 missing indexes
- Do NOT use EF Core `SqlQuery<T>` for the stats query (already failed 2 attempts)

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** - ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (dotnet test with NUnit + Postgres test fixture)
- **Automated tests**: Tests-after (run existing suite, fix failures)
- **Framework**: NUnit + Entity Framework Core InMemory / PostgreSQL fixture
- **Agent-Executed QA**: EVERY task verified via curl/psql/docker commands

### QA Policy
Every task includes agent-executed QA scenarios using curl (API endpoints) and docker exec (database verification).
Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.log`.

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Start Immediately — 4 PARALLEL tasks):
├── Task 1: Fix /diagnostics/stats (raw Npgsql + Dapper)
├── Task 2: Create EF Core migration for 4 missing GIN indexes
├── Task 3: Add Debug logging to scraper pipeline (TorrentInfoService + GenericProcessor)
└── Task 4: Trigger scraper, observe logs, diagnose failure

Wave 2 (After Wave 1 — depends on Task 1, 3, 4):
├── Task 5: Fix scraper ingestion root cause (based on log evidence from Task 4)
└── Task 6: Apply DB migration (Task 2), rebuild, redeploy

Wave 3 (After Wave 2 — depends on Task 5, 6):
├── Task 7: Run full test suite, fix any failures
└── Task 8: End-to-end verification (all endpoints + scraper + health + tests)
```

Critical Path: Task 1 → Task 2 → Task 4 → Task 5 → Task 6 → Task 7 → Task 8
Parallel Speedup: ~50% faster than sequential (Tasks 1-4 in parallel, 5-6 parallel, 7-8 parallel)

### Dependency Matrix
- **1-4**: None → Wave 1 (all parallel)
- **5**: 1, 3, 4 → Wave 2
- **6**: 2 → Wave 2
- **7**: 5, 6 → Wave 3
- **8**: 7 → Wave 3

---

## TODOs

- [x] 1. Fix `/diagnostics/stats` using raw Npgsql + Dapper

  **What to do**:
  - Open `src/Zilean.ApiService/Features/Diagnostics/DiagnosticEndpoints.cs`
  - Replace the `dbContext.Database.SqlQuery<TableStatRaw>(...)` query (with JOIN) with a raw `NpgsqlConnection` + Dapper `QueryAsync<TableStatRaw>` call
  - Follow the exact pattern from `ImdbPostgresMatchingService.cs:32-65` — inject `NpgsqlConnection`, open if closed, execute query, reuse existing connection
  - The SQL query itself is correct, only the execution method changes:
    ```sql
    SELECT
        c.relname AS "Name",
        s.n_live_tup AS "RowCount",
        pg_total_relation_size(c.oid) AS "SizeBytes"
    FROM pg_stat_user_tables s
    JOIN pg_class c ON c.relname = s.relname
    ORDER BY pg_total_relation_size(c.oid) DESC
    ```
  - Also fix the second `SqlQuery<long>` for `pg_database_size` — convert to Dapper too
  - Add the computed fields `sizeMb` and `totalDatabaseSizeMb` in the handler (math: `SizeBytes / 1048576.0`)
  - Keep `TableStatRaw` class as-is (it's a plain DTO, works with Dapper)
  - Keep the `lastIngestionTime` logic (query Torrents table for latest `IngestedOn`)

  **Must NOT do**:
  - Do NOT change the SQL query — it's correct
  - Do NOT attempt another EF Core `SqlQuery<T>` approach
  - Do NOT modify `TableStatRaw` DTO
  - Do NOT change the response shape (must match AGENTS.md documentation)

  **Recommended Agent Profile**:
  - **Category**: `quick` (single-file change, follow existing pattern)
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3, 4)
  - **Blocks**: Task 5
  - **Blocked By**: None

  **References**:
  - `src/Zilean.Database/Services/Postgres/ImdbPostgresMatchingService.cs:32-65` — Pattern to follow for raw NpgsqlConnection + Dapper query execution
  - `src/Zilean.ApiService/Features/Diagnostics/DiagnosticEndpoints.cs:105-145` — Current broken SqlQuery code to replace
  - `src/Zilean.ApiService/Zilean.ApiService.csproj` — Check if Dapper is already referenced (it is via Database project)

  **Acceptance Criteria**:
  - [ ] `/diagnostics/stats` returns HTTP 200 (not 500)
  - [ ] Response JSON contains `tables` array with entries having `{name, rowCount, sizeBytes, sizeMb}`
  - [ ] Response JSON contains `totalDatabaseSizeBytes`, `totalDatabaseSizeMb`, `lastIngestionTime`
  - [ ] `tables` array includes `ImdbFiles` (rowCount ~1,336,042) and `ParsedPages` (rowCount ~773)

  **QA Scenarios**:

  ```
  Scenario: Stats endpoint returns table data successfully
    Tool: Bash (curl)
    Preconditions: Docker stack running on port 8181
    Steps:
      1. curl -s http://localhost:8181/diagnostics/stats
      2. jq '.tables | length' → assert > 0
      3. jq '.tables[0].name' → assert not empty string
      4. jq '.tables[0].rowCount' → assert is number
      5. jq '.tables[0].sizeMb' → assert is number
      6. jq '.totalDatabaseSizeMb' → assert is number > 0
    Expected Result: HTTP 200, valid JSON with table stats, no 500 error
    Evidence: .sisyphus/evidence/task-1-stats-success.log

  Scenario: Stats endpoint response matches AGENTS.md documentation
    Tool: Bash (curl + jq)
    Steps:
      1. curl -s http://localhost:8181/diagnostics/stats | jq 'keys'
      2. Assert contains: ["tables", "totalDatabaseSizeBytes", "totalDatabaseSizeMb", "lastIngestionTime"]
      3. jq '.tables[0] | keys' → assert contains: ["name", "rowCount", "sizeBytes", "sizeMb"]
    Expected Result: All documented fields present
    Evidence: .sisyphus/evidence/task-1-stats-schema.log
  ```

  **Evidence to Capture**:
  - [ ] task-1-stats-success.log — full curl output
  - [ ] task-1-stats-schema.log — jq schema validation output

  **Commit**: YES
  - Message: `fix(diagnostics): use raw Npgsql+Dapper for /diagnostics/stats query`
  - Files: `src/Zilean.ApiService/Features/Diagnostics/DiagnosticEndpoints.cs`

- [x] 2. Create EF Core migration for 4 missing GIN indexes

  **What to do**:
  - Run `dotnet ef migrations add AddMissingGinIndexes` in the Database project
  - Verify the generated migration file adds these 4 indexes (from `TorrentInfoConfiguration.cs:248-263`):
    1. `idx_cleaned_parsed_title_trgm` — GIN trigram on `"CleanedParsedTitle"`
    2. `idx_seasons_gin` — GIN on `"Seasons"` (array column)
    3. `idx_episodes_gin` — GIN on `"Episodes"` (array column)
    4. `idx_languages_gin` — GIN on `"Languages"` (array column)
  - Check that EF Core generates `CREATE INDEX CONCURRENTLY` or use manual SQL in the migration for safety (GIN on empty table is fine either way)
  - Update the Database project's `DatabaseServiceRegistration.cs` if needed to ensure migration runs at startup

  **Must NOT do**:
  - Do NOT create raw SQL files outside the migration system
  - Do NOT add the indexes manually via psql (use migration)
  - Do NOT change the index definitions from what `TorrentInfoConfiguration` specifies

  **Recommended Agent Profile**:
  - **Category**: `quick` (migration generation via dotnet CLI)
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 3, 4)
  - **Blocks**: Task 6
  - **Blocked By**: None

  **References**:
  - `src/Zilean.Database/Configurations/TorrentInfoConfiguration.cs:248-263` — Index definitions to migrate
  - `src/Zilean.Database/Migrations/20260425212943_PendingModelChanges.cs` — Example migration (latest)
  - `src/Zilean.Database/Zilean.Database.csproj` — EF Core tooling config

  **Acceptance Criteria**:
  - [ ] Migration file created with `.Designer.cs` snapshot
  - [ ] Migration `Up()` method contains 4 `CREATE INDEX` statements
  - [ ] After applying: `docker exec zilean-db psql -U postgres -d zilean -c "\di idx_*"` shows 4 indexes

  **QA Scenarios**:

  ```
  Scenario: Migration file exists and contains all 4 indexes
    Tool: Bash (bat + grep)
    Steps:
      1. fdfind "AddMissingGinIndexes" src/Zilean.Database/Migrations/
      2. bat --paging=never {found file} | rg -c "CREATE INDEX"
      3. Assert count >= 4
      4. bat --paging=never {found file} | rg "idx_cleaned_parsed_title_trgm"
      5. bat --paging=never {found file} | rg "idx_seasons_gin"
      6. bat --paging=never {found file} | rg "idx_episodes_gin"
      7. bat --paging=never {found file} | rg "idx_languages_gin"
    Expected Result: Migration file exists with 4+ CREATE INDEX statements naming all 4 indexes
    Evidence: .sisyphus/evidence/task-2-migration-exists.log
  ```

  **Evidence to Capture**:
  - [ ] task-2-migration-exists.log — migration file contents with CREATE INDEX lines

  **Commit**: YES
  - Message: `feat(db): add migration for 4 missing GIN indexes on Torrents table`
  - Files: `src/Zilean.Database/Migrations/*AddMissingGinIndexes*`

- [x] 3. Add Debug logging to scraper ingestion pipeline

  **What to do**:
  - Open `src/Zilean.ApiService/Features/Torrents/Services/TorrentInfoService.cs`
  - Add `LogDebug` statement BEFORE `BulkInsertOrUpdateAsync` call: log batch size, first 3 info hashes as sample
  - Add `LogDebug` statement AFTER `BulkInsertOrUpdateAsync` call: log result (rows affected or exception)
  - Add `LogDebug` statement AFTER `MatchImdbIdsForBatchAsync` call: log matched count (how many torrents got an ImdbId assigned)
  - Open `src/Zilean.ApiService/Features/Ingestion/GenericProcessor.cs`
  - In the `OnProcessTorrentsAsync` catch block (line ~125-128): change `_logger.LogWarning` to `_logger.LogError` so failures are visible
  - Add `LogDebug` BEFORE the catch: log "Starting batch processing, count={Count}"
  - Add `LogDebug` AFTER the catch (in finally equivalent): log "Batch processing complete, count={Count}, success={Success}"

  **Must NOT do**:
  - Do NOT refactor the error handling logic — only change log level and add Debug statements
  - Do NOT modify the matching logic or bulk insert logic
  - Do NOT change the `match_torrents_to_imdb` function

  **Recommended Agent Profile**:
  - **Category**: `quick` (add log statements, 2 files)
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 4)
  - **Blocks**: Task 5
  - **Blocked By**: None

  **References**:
  - `src/Zilean.ApiService/Features/Torrents/Services/TorrentInfoService.cs:75-95` — StoreTorrentInfo method where bulk insert happens
  - `src/Zilean.ApiService/Features/Ingestion/GenericProcessor.cs:110-135` — OnProcessTorrentsAsync catch block
  - `src/Zilean.Shared/Features/Configuration/LoggingConfiguration.cs` — Current log config (Debug default, Zilean: Debug)

  **Acceptance Criteria**:
  - [ ] `TorrentInfoService.cs` has LogDebug before and after `BulkInsertOrUpdateAsync`
  - [ ] `TorrentInfoService.cs` has LogDebug after `MatchImdbIdsForBatchAsync`
  - [ ] `GenericProcessor.cs` catch block uses `LogError` instead of `LogWarning`
  - [ ] Code compiles without errors

  **QA Scenarios**:

  ```
  Scenario: Log statements compile and appear in container logs after scraper run
    Tool: Bash (docker logs)
    Preconditions: Rebuilt container with logging changes deployed
    Steps:
      1. docker logs zilean --tail 50 | grep -c "Starting batch processing"
      2. Assert > 0 (log statement fired)
      3. docker logs zilean --tail 50 | grep -c "Error processing batch"
      4. If scraper fails, assert error shows in ERROR level logs (not just WARNING)
    Expected Result: Debug log statements visible in container output (Serilog Debug level)
    Evidence: .sisyphus/evidence/task-3-scraper-logs.log
  ```

  **Evidence to Capture**:
  - [ ] task-3-scraper-logs.log — docker logs showing Debug statements from scraper pipeline

  **Commit**: YES
  - Message: `fix(scraper): add Debug logging to torrent storage pipeline`
  - Files: `src/Zilean.ApiService/Features/Torrents/Services/TorrentInfoService.cs`, `src/Zilean.ApiService/Features/Ingestion/GenericProcessor.cs`

- [x] 4. Trigger scraper and diagnose ingestion failure

  **What to do**:
  - First: check scraper binary exists and is executable — `docker exec zilean ls -la /app/scraper`
  - Trigger DMM sync manually: `curl -H 'X-API-Key: test-api-key-123' http://localhost:8181/dmm/on-demand-scrape`
  - Wait 10 minutes for scraper to run (DMM download + parsing + matching + storage)
  - Monitor logs continuously: `docker logs -f zilean` — watch for Debug statements from Task 3
  - After scraper completes (or times out), check:
    - `curl http://localhost:8181/diagnostics/stats | jq '.tables[] | select(.name=="Torrents")'` — any rows?
    - `curl http://localhost:8181/diagnostics/freshness` — any torrents?
    - `curl http://localhost:8181/diagnostics/queue` — any failed items?
    - `docker logs zilean --tail 500 | grep -i "error\|Exception\|fail\|warning"` — any errors?
  - If `Torrents` table still has 0 rows: examine logs for the Debug statements from Task 3 to identify exact failure point
  - Check if FK constraint is blocking: `docker exec zilean-db psql -U postgres -d zilean -c "SELECT * FROM \"IngestionQueue\" LIMIT 5;"`
  - Document the exact failure point and error message for Task 5

  **Must NOT do**:
  - Do NOT modify code during this investigation — only observe
  - Do NOT restart containers during scraper run

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` (observational task, manual curl/docker commands)
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 3)
  - **Blocks**: Task 5
  - **Blocked By**: None

  **References**:
  - `AGENTS.md` — Diagnostic endpoints documentation
  - `src/Zilean.ApiService/Features/Ingestion/DmmSyncJob.cs` — Scraper invocation entry point
  - `/healthchecks/health` — Check DB health before triggering scraper

  **Acceptance Criteria**:
  - [ ] Scraper binary confirmed executable at `/app/scraper`
  - [ ] Scraper triggered and ran (check docker logs for activity)
  - [ ] Logs captured showing Debug output from Task 3 code
  - [ ] If torrents persist (> 0 in Torrents table), Task 5 is not needed
  - [ ] If torrents still = 0, exact failure point identified with error message

  **QA Scenarios**:

  ```
  Scenario: Scraper runs and produces diagnostic output
    Tool: Bash (curl + docker logs)
    Preconditions: Docker stack running, health checks pass
    Steps:
      1. curl -s http://localhost:8181/healthchecks/health | jq '.status'
      2. Assert == "Healthy"
      3. curl -H 'X-API-Key: test-api-key-123' http://localhost:8181/dmm/on-demand-scrape
      4. sleep 600 (wait 10 minutes)
      5. docker logs zilean --tail 200 > /tmp/scraper-run.log
      6. bat --paging=never /tmp/scraper-run.log | rg -i "storing\|matching\|error\|exception"
    Expected Result: Scraper activity visible in logs with Debug statements
    Evidence: .sisyphus/evidence/task-4-scraper-diagnosis.log

  Scenario: Diagnostic endpoints show scraper state after run
    Tool: Bash (curl)
    Steps:
      1. curl -s http://localhost:8181/diagnostics/stats | jq '.tables[] | select(.name=="Torrents")'
      2. curl -s http://localhost:8181/diagnostics/freshness | jq .totalTorrents
      3. curl -s http://localhost:8181/diagnostics/queue | jq .failed
    Expected Result: Stats and freshness show current state (0 or > 0)
    Evidence: .sisyphus/evidence/task-4-scraper-state.json
  ```

  **Evidence to Capture**:
  - [ ] task-4-scraper-diagnosis.log — 200 lines of container logs during/after scraper run
  - [ ] task-4-scraper-state.json — diagnostic endpoint output after scraper run

  **Commit**: NO (observational task, no code changes)

- [x] 5. Fix scraper ingestion root cause (based on Task 4 evidence)

  **What to do**:
  - Based on the log evidence from Task 4, implement the fix. Possible root causes (from investigation + `docs/test-report-2026-04-25.md`):

  - **A: Python RTN engine failed to initialize** (reported in test-report: `The type initializer for 'Delegates' threw an exception`):
    - Check if `libpython3.11.so` is available in the container: `docker exec zilean find / -name "libpython3.11*" 2>/dev/null`
    - If missing: install `python3.11` in the Dockerfile runtime stage (`RUN apt-get update && apt-get install -y python3.11`)
    - Check `PYTHONNET_PYDLL` env var is set or python DLL path is correct
    - Verify with: trigger scraper, check logs for "Python engine initialized" vs "type initializer for 'Delegates' threw"
    - This affects ALL torrent parsing — if Python is broken, 0 torrents will be parsed

  - **B: FK constraint violation** (ImdbId references non-existent ImdbFiles row):
    - The matching function returned a non-existent ImdbId. Fix: add a JOIN to validate the match exists, or change FK to allow unmatched torrents (NULL ImdbId).

  - **C: BulkInsertOrUpdateAsync throws exception**: Check exception type — is it a timeout? FK violation? Batch size issue? Fix accordingly:
    - Timeout → increase command timeout or reduce batch size
    - FK violation → handle as above
    - Batch size → split into smaller batches (1000 per batch instead of 5000)

  - **D: Matching function never returns** (CROSS JOIN LATERAL hangs): Reduce batch size for matching (e.g., 500 per batch) and add a query timeout.

  - **E: Matching returns but no ImdbIds assigned** (all NULL): The word_similarity threshold (0.85) may be too strict.

  - **F: No error at all** (BulkInsert succeeds, but EF Core doesn't commit): Check if there's a transaction wrapping the operation. Ensure `SaveChangesAsync` is called after BulkInsert.

  - The fix MUST be minimal — exactly what changes the 0 to > 0. No refactoring.

  **Must NOT do**:
  - Do NOT rewrite the matching algorithm
  - Do NOT change the database schema beyond adding the indexes (Task 2)
  - Do NOT optimise prematurely (only fix the blocking issue)

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high` (root cause fix, requires understanding of EF Core + Npgsql + PostgreSQL)
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Task 6)
  - **Blocks**: Task 7
  - **Blocked By**: Tasks 1, 3, 4

  **References**:
  - `src/Zilean.ApiService/Features/Torrents/Services/TorrentInfoService.cs` — StoreTorrentInfo method
  - `src/Zilean.Database/Services/Postgres/ImdbPostgresMatchingService.cs` — Matching service
  - `src/Zilean.Database/Configurations/TorrentInfoConfiguration.cs:232-234` — FK constraint definition
  - `src/Zilean.Database/Functions/match_torrents_to_imdb.sql` — Matching function

  **Acceptance Criteria**:
  - [ ] After fix + redeploy: `Torrents` table has > 0 rows after scraper run
  - [ ] No ERROR-level logs from scraper pipeline
  - [ ] `/diagnostics/freshness` shows `totalTorrents > 0`

  **QA Scenarios**:

  ```
  Scenario: Scraper run produces torrents in database with fixed code
    Tool: Bash (curl)
    Preconditions: Fix applied, container rebuilt and deployed, scraper triggered
    Steps:
      1. curl -s http://localhost:8181/diagnostics/stats | jq '.tables[] | select(.name=="Torrents") | .rowCount'
      2. Assert > 0
      3. curl -s http://localhost:8181/diagnostics/freshness | jq '.totalTorrents'
      4. Assert > 0
      5. curl -X POST http://localhost:8181/dmm/search -H 'Content-Type: application/json' -d '{"queryText":"Batman"}'
      6. Assert returns results (totalResults > 0 or results array length > 0)
    Expected Result: Torrents table populated, search returns results
    Evidence: .sisyphus/evidence/task-5-torrents-ingested.log

  Scenario: No errors in logs after successful ingestion
    Tool: Bash (docker logs)
    Steps:
      1. docker logs zilean --tail 100 | rg -i "error|exception" | wc -l
      2. Assert == 0 (no new errors)
    Expected Result: Clean logs after ingestion
    Evidence: .sisyphus/evidence/task-5-clean-logs.log
  ```

  **Evidence to Capture**:
  - [ ] task-5-torrents-ingested.log — diagnostic endpoints showing Torrents > 0
  - [ ] task-5-clean-logs.log — docker logs confirming no errors

  **Commit**: YES
  - Message: `fix(scraper): [specific fix description based on root cause]`
  - Files: TBD based on root cause

- [x] 6. Apply index migration, rebuild, redeploy

  **What to do**:
  - Apply the migration from Task 2: `dotnet ef database update`
  - Alternatively: redeploy the container (migrations run at startup)
  - Rebuild Docker image: `docker compose -f docker-compose-test.yaml build zilean`
  - Redeploy: `docker compose -f docker-compose-test.yaml up -d zilean`
  - Wait for startup (check logs for "Migrations Applied" message)
  - Verify indexes exist: `docker exec zilean-db psql -U postgres -d zilean -c "\di idx_*"`
  - Verify health check passes: `curl -s http://localhost:8181/healthchecks/health | jq '.indexes.isHealthy'`
  - If scraper fix (Task 5) was done, include it in this rebuild

  **Must NOT do**:
  - Do NOT apply indexes via raw psql — only via migration/startup

  **Recommended Agent Profile**:
  - **Category**: `quick` (docker build + deploy, well-known workflow)
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Task 5)
  - **Blocks**: Task 7
  - **Blocked By**: Task 2 (Task 5 also needed if scraper fix exists)

  **References**:
  - `docker-compose-test.yaml` — Docker compose config
  - `Dockerfile` — Docker build (multi-stage)
  - `src/Zilean.Database/Migrations/*AddMissingGinIndexes*` — Migration to apply

  **Acceptance Criteria**:
  - [ ] Container builds without errors
  - [ ] Container starts and logs "Migrations Applied"
  - [ ] All 4 indexes exist: `\di idx_*` shows `idx_cleaned_parsed_title_trgm`, `idx_seasons_gin`, `idx_episodes_gin`, `idx_languages_gin`
  - [ ] `/healthchecks/health` returns `indexes.isHealthy: true`

  **QA Scenarios**:

  ```
  Scenario: Indexes present after migration
    Tool: Bash (docker exec + psql)
    Steps:
      1. docker exec zilean-db psql -U postgres -d zilean -c "\di idx_cleaned_parsed_title_trgm"
      2. Assert returns 1 row
      3. docker exec zilean-db psql -U postgres -d zilean -c "\di idx_seasons_gin"
      4. Assert returns 1 row
      5. docker exec zilean-db psql -U postgres -d zilean -c "\di idx_episodes_gin"
      6. Assert returns 1 row
      7. docker exec zilean-db psql -U postgres -d zilean -c "\di idx_languages_gin"
      8. Assert returns 1 row
    Expected Result: All 4 GIN indexes exist on Torrents table
    Evidence: .sisyphus/evidence/task-6-indexes.log

  Scenario: Health check confirms indexes healthy
    Tool: Bash (curl)
    Steps:
      1. curl -s http://localhost:8181/healthchecks/health | jq '.indexes'
      2. Assert .isHealthy == true
      3. Assert .missingIndexes is empty or null
    Expected Result: Health check reports indexes healthy
    Evidence: .sisyphus/evidence/task-6-health-indexes.log
  ```

  **Evidence to Capture**:
  - [ ] task-6-indexes.log — psql \di output
  - [ ] task-6-health-indexes.log — health check JSON output

  **Commit**: NO (deployment step, no code changes)

- [x] 7. Run full test suite and fix failures

  **What to do**:
  - Run `dotnet test --logger "console;verbosity=detailed"` from project root
  - Capture full output (pass/fail counts, error messages)
  - For any failing test: read the test file, understand what it asserts, fix the code OR fix the test
  - Common expected failures and fixes:
    - Tests asserting on `Torrents` row count (now > 0 after scraper fix) — update expected counts
    - Tests using `SqlQuery<T>` pattern — ensure they work with the Dapper change
    - Tests that expect specific migration state — update for new migration
    - PostgreSQL lifecycle fixture issues — ensure database is in correct state
  - Do NOT skip tests — fix or update them
  - Do NOT delete tests that became "flaky" — fix the underlying issue

  **Must NOT do**:
  - Do NOT skip tests with `[Ignore]` or `[Skip]`
  - Do NOT delete failing tests
  - Do NOT change test assertions to `Assert.Pass()` as a workaround

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high` (test suite analysis and fixing)
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Task 8)
  - **Blocks**: Task 8 (partial)
  - **Blocked By**: Tasks 5, 6

  **References**:
  - `tests/Zilean.Tests/` — Test project directory
  - `tests/Zilean.Tests/Zilean.Tests.csproj` — Test project config
  - `tests/Zilean.Tests/Fixtures/` — PostgreSQL lifecycle fixture

  **Acceptance Criteria**:
  - [ ] `dotnet test` exits with code 0
  - [ ] 0 Failed, 0 Skipped
  - [ ] All test fixtures (including PostgresLifecycleFixture) pass

  **QA Scenarios**:

  ```
  Scenario: All tests pass after fixes
    Tool: Bash (dotnet test)
    Preconditions: All fixes deployed, scraper has run
    Steps:
      1. dotnet test --logger "console;verbosity=detailed" 2>&1 | tee /tmp/test-output.log
      2. rg "Failed: 0" /tmp/test-output.log
      3. rg "Skipped: 0" /tmp/test-output.log
      4. rg "Passed:" /tmp/test-output.log (assert count > 0)
      5. echo $? (last exit code — assert 0)
    Expected Result: All tests pass, exit code 0
    Evidence: .sisyphus/evidence/task-7-test-output.log
  ```

  **Evidence to Capture**:
  - [ ] task-7-test-output.log — complete `dotnet test` output

  **Commit**: YES (only if tests needed updating)
  - Message: `test: fix test failures after scraper/index changes`
  - Files: test files that were modified

- [x] 8. End-to-end verification (follow `docs/test-report-2026-04-25.md` test plan)

  **What to do**:
  - Execute ALL 36 tests from `docs/test-report-2026-04-25.md` following the 8-section structure:
    1. **Infrastructure** (TEST-Z-001 to Z-006): Migrations, env vars, healthcheck, freshness diag, query diag, queue diag
    2. **Ingestion** (TEST-Z-010 to Z-015): DMM sync runs, segments, checkpoints, memory bounds, refresh-on-miss, dedup
    3. **API Contract** (TEST-C-001 to C-006): Movie schema, series+episode, anime queries, empty query, special chars, response time
    4. **End-to-End** (TEST-E-001 to E-005): Comet manifest, movie stream, series stream, anime stream, repeated speed
    5. **Logging/Audit** (TEST-L-001 to L-007): Query capture, miss tracking, ingestion logs, failure capture, ranking debug, valid JSONL, log rotation
    6. **Anime Normalization** (TEST-A-001 to A-003): Title recall, fansub noise, season vs episode
    7. **Regression** (TEST-R-001 to R-004): Array response, required fields, healthcheck, Comet healthy
    8. **Evidence** (TEST-B-001 to B-003): Result counts, freshness, audit coverage
  - For each test: run the verification command, record pass/fail/warn, document any deviation from expected
  - Post-scraper-fix, tests that were WARN/N/A due to empty DB should now PASS (ingestion tests, evidence tests, anime tests)
  - All 22 previously-PASS tests must remain PASS (regression check)
  - Generate updated test report following same format as the original

  **Target: 36/36 PASS (up from 22/36)**

  **Must NOT do**:
  - Do NOT skip tests that require data (they should now have data after Task 5 fix)
  - Do NOT mark tests as N/A without verifying data truly doesn't exist post-fix

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high` (comprehensive 36-test execution)
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Task 7)
  - **Blocks**: Final Verification Wave
  - **Blocked By**: Task 7

  **References**:
  - `docs/test-report-2026-04-25.md` — 36-test integration plan (mandatory baseline)
  - `AGENTS.md` — Diagnostic endpoints documentation
  - `docker-compose-test.yaml` — Service config

  **Acceptance Criteria**:
  - [ ] All 36 tests pass (up from 22 baseline)
  - [ ] All 14 previously-WARN tests now PASS (data-dependent tests should resolve post-ingestion fix)
  - [ ] All 22 previously-PASS tests remain PASS (no regressions)
  - [ ] Test report updated with new timestamps and results

  **QA Scenarios**:

  ```
  Scenario: Full 36-test integration suite passes
    Tool: Bash (curl + docker + psql)
    Preconditions: All fixes deployed, scraper has completed at least one successful run
    Steps:
      1. Execute Section 1 (Infrastructure): 6 tests → all PASS
      2. Execute Section 2 (Ingestion): 6 tests → all PASS (previously 3 WARN)
      3. Execute Section 3 (API Contract): 6 tests → all PASS
      4. Execute Section 4 (End-to-End): 5 tests → all PASS
      5. Execute Section 5 (Logging/Audit): 7 tests → all PASS
      6. Execute Section 6 (Anime Normalization): 3 tests → all PASS (previously N/A)
      7. Execute Section 7 (Regression): 4 tests → all PASS
      8. Execute Section 8 (Evidence): 3 tests → all PASS (previously 2 WARN)
    Expected Result: 36/36 PASS, 0 WARN, 0 FAIL
    Evidence: .sisyphus/evidence/task-8-test-report.md (updated report)
  ```

  **Evidence to Capture**:
  - [ ] task-8-test-report.md — updated 36-test report with all results

  **Commit**: NO (verification only)

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.

- [x] F1. **Plan Compliance Audit** — Verify all 4 "Must Have" items:
  1. `/diagnostics/stats` returns HTTP 200 with table stats JSON → `curl -s http://localhost:8181/diagnostics/stats | jq '.tables | length'` must be > 0
  2. `/healthchecks/health` reports `indexes.isHealthy: true` → `curl -s http://localhost:8181/healthchecks/health | jq '.checks[] | select(.status=="indexes") | .isHealthy'` must be `true`
  3. `Torrents` table has rows → `curl -s http://localhost:8181/diagnostics/stats | jq '.tables[] | select(.name=="Torrents").rowCount'` must be > 0
  4. `dotnet test` exits 0 → `POSTGRES_PASSWORD=test dotnet test --nologo -v q` exit code must be 0
  - Verify all "Must NOT Have" guardrails (no changes to matching function, scraper binary, or schema beyond indexes)
  - Check `.sisyphus/evidence/` for all evidence files
  Output: `Must Have [4/4] | Must NOT Have [4/4] | Evidence [present/missing] | VERDICT: APPROVE`

- [x] F2. **Code Quality Review** — Run `dotnet build --no-restore` in container, check for:
  - Compiler warnings (none expected)
  - `as any` or `@ts-ignore` (C#: no unchecked casts or `#pragma warning disable`)
  - Empty catch blocks (none expected — GenericProcessor catch now logs at Error)
  - Unused imports/usings
  - Debug logging left at inappropriate levels (Debug is fine)
  Output: `Build [PASS] | Warnings [0] | Quality issues [0] | VERDICT: APPROVE`

- [x] F3. **Real Manual QA** — Execute the 36-test plan from `docs/test-report-2026-04-25.md`:
  - Run all 8 sections, 36 tests
  - Verify 36/36 PASS (up from 22/36 baseline)
  - Cross-reference with individual task QA scenarios (Tasks 1-8)
  - Test cross-task integration: search with newly ingested torrents
  - Save updated test report to `docs/test-report-2026-04-26.md`
  Output: `Tests [36/36 PASS] | Integration [PASS] | VERDICT: APPROVE`

- [x] F4. **Scope Fidelity Check** — Compare git diff against plan:
  - Verify only planned files were touched
  - Check for uncommitted changes (should be none — all changes committed)
  - Verify `match_torrents_to_imdb` function was NOT modified
  - Verify scraper binary was NOT modified
  - Verify no schema changes beyond the 4 indexes
  - Detect cross-task contamination
  Output: `Files touched [17] | Unexpected changes [0] | Contamination [CLEAN] | VERDICT: APPROVE`

---

## Commit Strategy

- **1**: `fix(diagnostics): use raw Npgsql+Dapper for /diagnostics/stats query` — DiagnosticEndpoints.cs
- **2**: `feat(db): add migration for 4 missing GIN indexes` — New migration file
- **3**: `fix(scraper): add Debug logging to scraper ingestion pipeline` — TorrentInfoService.cs, GenericProcessor.cs
- **5**: `fix(scraper): [TBD after diagnosis]` — TBD files
- **6**: `chore: apply index migration and rebuild` — N/A (already committed)
- **7**: `test: fix test failures after scraper/index changes` — test files
- **8**: `chore: final verification evidence` — .sisyphus/evidence/

---

## Success Criteria

### Verification Commands
```bash
# Diagnostic stats endpoint
curl -s http://localhost:8181/diagnostics/stats | jq '.tables | length'
# Expected: number > 0

# Health check indexes
curl -s http://localhost:8181/healthchecks/health | jq '.indexes.isHealthy'
# Expected: true

# Torrents ingested
curl -s http://localhost:8181/diagnostics/freshness | jq '.totalTorrents'
# Expected: number > 0

# All tests pass
dotnet test --logger "console;verbosity=detailed"
# Expected: exit code 0, 0 failed, 0 skipped

# 36-test integration plan
# Follow docs/test-report-2026-04-25.md
# Expected: 36/36 PASS
```

### Final Checklist
- [ ] All "Must Have" present
- [ ] All "Must NOT Have" absent
- [ ] All tests pass (dotnet test + 36-test integration plan)
- [ ] Scraper ingests torrents
- [ ] Updated test report saved to `docs/test-report-2026-04-26.md`
