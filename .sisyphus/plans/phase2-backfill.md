# Phase 2a: Historical Backfill via Prowlarr

## TL;DR

> **Quick Summary**: Add `POST /admin/sources/backfill/{sourceName}?untilDate=YYYY-MM-DD` that runs a keyword-scraping backfill. Bypasses Prowlarr's ~100-item RSS limit by iterating through 68 different search queries (years, words, letters), each returning different results. Slow (5s between pages + 5s between keywords), deduplicating, background-friendly. Accumulates thousands of historical torrents over hours/days of 24/7 running.

> **Deliverables**:
> - 1 admin endpoint (`POST /admin/sources/backfill/{sourceName}?untilDate=YYYY-MM-DD`)
> - Keyword-scraping engine with 68 built-in search terms
> - Backfill rate limiting (5s page delay + 5s keyword delay)
> - 3 tasks total
>
> **Estimated Effort**: Trivial (3 quick tasks)
> **Runtime per indexer**: ~57 minutes for 68 keywords

---

## Context

### What Works Already

The existing `SyncIndexerAsync` paginates through Prowlarr Torznab pages, stops when items are older than `TorrentSourceStats.LastSyncAt`, and upserts with dedup.

### Bypassing Prowlarr's ~100-item RSS Limit

Prowlarr caps empty-query RSS at ~100 items. To get historical data, we use **keyword scraping**: many different search queries, each returning different results. A list of ~50 keywords (years, numbers, common words, letters) × 4 indexers × ~100 results each = up to ~20,000 torrents accumulated over days of slow background running.

Keyword list strategy:
```
Years:  2000,2001,...,2026   (27 terms)
Numbers: 1,2,3,4,5          (5 terms)  
Words:   the,and,1080p,2160p,BluRay,WEB-DL,HEVC,H264,x264,x265 (10 terms)
Letters: a,b,c,...,z        (26 terms)
Total:  68 terms × slow rate limiting = harvests everything discoverable
```

### How It Works

1. `POST /admin/sources/backfill/nyaa?untilDate=2017-01-01` starts the backfill
2. Backfill job: for each indexer, iterate through keyword list
3. Each keyword → Torznab query → paginate all results → upsert → next keyword
4. Total queries: 68 keywords × up to 5 pages each × 2 working indexers = ~680 requests
5. At 5s between pages: ~57 minutes total per indexer (runs in background, doesn't block normal ops)
6. Track which keywords have been processed per indexer (don't repeat)
7. Future backfills: only process NEW keywords added to the list

---

## TODOs

- [ ] 1. Admin endpoint — POST /admin/sources/backfill/{sourceName}

  **What to do**:
  - In `AdminEndpoints.cs`, add:
    ```csharp
    group.MapPost("/sources/backfill/{sourceName}", BackfillSource);
    
    private static async Task<IResult> BackfillSource(
        string sourceName,
        [FromQuery] string untilDate,  // "YYYY-MM-DD"
        ProwlarrSyncJob syncJob)
    {
        if (!DateTime.TryParse(untilDate, out var date))
            return TypedResults.BadRequest("Invalid date format. Use YYYY-MM-DD.");
        
        var count = await syncJob.BackfillIndexerAsync(sourceName, date);
        
        return TypedResults.Ok(new { sourceName, untilDate, torrentsProcessed = count });
    }
    ```

  **Must NOT do**:
  - No complex validation — simple TryParse
  - No new files — add to existing AdminEndpoints.cs

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []
  - Reason: Single endpoint, 10 lines

  **Parallelization**: Task 1 only
  - **Blocks**: Task 2

  **Acceptance Criteria**:
  - [ ] `curl -X POST -H "X-API-Key: test-api-key-123" "http://localhost:8181/admin/sources/backfill/nyaa?untilDate=2020-01-01"` returns JSON with torrentsProcessed count
  - [ ] Invalid date returns 400 Bad Request
  - [ ] Missing untilDate returns 400

  **Commit**: YES
  - Message: `feat(admin): add backfill endpoint for historical Prowlarr sync`

- [ ] 2. BackfillIndexerAsync — keyword scraping to bypass RSS limit

  **What to do**:
  - In `ProwlarrSyncJob.cs`, add keyword list and `BackfillIndexerAsync`:
    ```csharp
    private static readonly string[] BackfillKeywords = [
        // Years (2000-2026, no 2027+ as unreleased)
        "2000","2001","2002","2003","2004","2005","2006","2007","2008","2009",
        "2010","2011","2012","2013","2014","2015","2016","2017","2018","2019",
        "2020","2021","2022","2023","2024","2025","2026",
        // Numbers
        "1","2","3","4","5",
        // Common torrent title words
        "1080p","2160p","BluRay","WEB-DL","HEVC","H264","x264","x265","the","and",
        // Alphabet (single chars match many torrents)
        "a","b","c","d","e","f","g","h","i","j","k","l","m","n","o","p","q","r","s","t","u","v","w","x","y","z"
    ];
    
    public async Task<int> BackfillIndexerAsync(string sourceName, DateTime untilDate)
    {
        var indexer = configuration.Prowlarr.Indexers
            .FirstOrDefault(i => i.SourceName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
        if (indexer == null)
            throw new InvalidOperationException($"Indexer '{sourceName}' not found.");
        
        // Reset checkpoint to untilDate
        var stats = await GetOrCreateStatsAsync(sourceName);
        stats.LastSyncAt = untilDate;
        await dbContext.SaveChangesAsync(CancellationToken);
        
        logger.LogInformation("[ProwlarrBackfill] Starting keyword backfill for '{SourceName}' with untilDate {UntilDate}",
            sourceName, untilDate.ToString("yyyy-MM-dd"));
        
        var totalProcessed = 0;
        foreach (var keyword in BackfillKeywords)
        {
            if (CancellationToken.IsCancellationRequested) break;
            
            // Search with keyword to bypass RSS limit
            var count = await SyncIndexerWithQueryAsync(indexer, keyword, backfillMode: true);
            totalProcessed += count;
            
            // 5s between keywords (respects rate limits)
            await Task.Delay(5000, CancellationToken);
        }
        
        logger.LogInformation("[ProwlarrBackfill] Complete for '{SourceName}': {Total} torrents from {Keywords} keywords",
            sourceName, totalProcessed, BackfillKeywords.Length);
        
        return totalProcessed;
    }
    ```
  - Add `SyncIndexerWithQueryAsync` — same as `SyncIndexerAsync` but with `query` parameter:
    ```csharp
    private async Task<int> SyncIndexerWithQueryAsync(ProwlarrIndexer indexer, string query, bool backfillMode = false)
    {
        var url = $"{configuration.Prowlarr.BaseUrl.TrimEnd('/')}/{indexer.IndexerId}/api"
            + $"?t=search&apikey={configuration.Prowlarr.ApiKey}"
            + $"&cat={indexer.Categories}&extended=1"
            + $"&q={Uri.EscapeDataString(query)}";
        // ... same pagination logic as SyncIndexerAsync with offset/limit ...
        // 5s between pages for backfill, 1s for normal
    }
    ```
  - In the pagination while loop:
    ```csharp
    var delayMs = backfillMode ? 5000 : 1000;
    await Task.Delay(delayMs, CancellationToken);
    ```

  **Must NOT do**:
  - Do NOT create a separate job class — all in ProwlarrSyncJob
  - Do NOT add a new table — just new methods
  - Do NOT change existing cron sync behavior

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low`
  - **Skills**: []
  - Reason: New method + keyword loop, ~40 lines, minimal complexity

  **Parallelization**: Sequential after Task 1
  - **Blocked By**: Task 1

  **Acceptance Criteria**:
  - [ ] Backfill with untilDate=1970-01-01 processes 1000+ torrents via keyword scraping (not just 73 from RSS)
  - [ ] Each keyword query returns different results (bypasses the 100-item limit)
  - [ ] 5s delay between pages, 5s delay between keywords
  - [ ] Normal cron sync unaffected
  - [ ] Dedup handles overlaps (same torrent from multiple keywords)

  **Commit**: YES
  - Message: `feat(backfill): add keyword-scraping backfill to bypass Prowlarr RSS limit`

- [ ] 3. Build, test, deploy, verify

  **What to do**:
  - `dotnet build Zilean.sln` — 0 errors
  - `docker compose -f docker-compose-test.yaml build zilean && docker compose up -d zilean`
  - Verify: `curl -X POST -H "X-API-Key: test-api-key-123" "http://localhost:8181/admin/sources/backfill/nyaa?untilDate=1970-01-01"` returns torrentsProcessed > 0
  - Verify: Second call with same date returns 0
  - Check log file for `[ProwlarrBackfill]` messages
  - Verify DMM search unaffected

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []
  - Reason: Build + deploy + curl verification

  **Parallelization**: Sequential after Task 2
  - **Blocked By**: Task 2

  **Acceptance Criteria**:
  - [ ] Build passes
  - [ ] Deploy succeeds
  - [ ] Backfill endpoint works
  - [ ] Logs show backfill activity
  - [ ] DMM search still works

  **QA Scenarios**:
  ```
  Scenario: Backfill nyaa for all time
    Tool: Bash (curl)
    Steps:
      1. POST /admin/sources/backfill/nyaa?untilDate=1970-01-01
      2. Assert torrentsProcessed > 0
      3. POST again with same date
      4. Assert torrentsProcessed = 0 (already backfilled)
    Evidence: .sisyphus/evidence/backfill-verify.txt

  Scenario: Backfill with bad date
    Tool: Bash (curl)
    Steps:
      1. POST /admin/sources/backfill/nyaa?untilDate=not-a-date
      2. Assert HTTP 400
    Evidence: .sisyphus/evidence/backfill-error.txt
  ```

  **Commit**: YES
  - Message: `chore: deploy and verify backfill endpoint`

---

## Verification Strategy

> No tests needed — trivial change, verified with live curl against deployed container.

---

## Success Criteria

```bash
# Trigger backfill for nyaa (10-year window)
curl -X POST -H "X-API-Key: test-api-key-123" \
  "http://localhost:8181/admin/sources/backfill/nyaa?untilDate=2017-01-01"
# Expected: {sourceName:"nyaa", untilDate:"2017-01-01", torrentsProcessed: > 1000}
# (1000+ because keyword scraping bypasses the 73-item RSS limit)

# Check progress via admin status
curl -s -H "X-API-Key: test-api-key-123" \
  http://localhost:8181/admin/sources/status | jq '.[] | {sourceName, torrentCount}'
# Expected: nyaa.torrentCount increased significantly vs pre-backfill

# DMM search still works
curl -X POST http://localhost:8181/dmm/search \
  -H 'Content-Type: application/json' \
  -d '{"queryText":"batman"}'
# Expected: returns results (both DMM and Prowlarr sources)

# Check source distribution
docker exec zilean-db psql -U postgres -d zilean \
  -c 'SELECT "Source", COUNT(*) FROM "Torrents" GROUP BY "Source" ORDER BY COUNT(*) DESC;'
# Expected: nyaa count significantly higher than pre-backfill 73
```
