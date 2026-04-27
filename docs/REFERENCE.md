# Zilean Reference Guide

> Everything learned about Zilean, Comet integration, Prowlarr backfill, and resilience patterns.
> Generated from the phase2-backfill implementation session.

---

## Architecture

```
┌──────────────┐     /dmm/filtered     ┌──────────────┐     Torznab API     ┌──────────────┐
│   Comet      │ ────────────────────→ │   Zilean     │ ─────────────────→ │  Prowlarr    │
│  (Stremio)   │ ←──────────────────── │  (API:8181)  │ ←───────────────── │ (Torznab)    │
└──────────────┘   JSON torrent list    └──────┬───────┘   XML RSS feed     └──────────────┘
                                               │
                                               │ upsert
                                               ▼
                                        ┌──────────────┐
                                        │ PostgreSQL   │
                                        │  (zilean-db) │
                                        └──────────────┘
```

- **Zilean** runs on port `8181`, PostgreSQL on `5432`
- **Comet** runs on port `8000`, queries Zilean for torrents
- **Prowlarr** is the upstream indexer proxy (`prowlarr.cloudpftc.com`)

---

## Comet Integration

### How Comet Searches Zilean

Comet uses **`GET /dmm/filtered`** with separate params (not embedded in query text):

| Media | URL Pattern |
|-------|------------|
| Movie | `GET /dmm/filtered?query={title}` |
| Series | `GET /dmm/filtered?query={title}&season={N}&episode={N}` |

**Fields Comet reads from response**: `raw_title`, `info_hash`, `size` — only these three.

Comet parses `raw_title` client-side using RTN (torrent name parser) for ranking/filtering.
Comet is **source-agnostic** — it doesn't care whether torrents came from DMM, Prowlarr, or elsewhere.

### Search Endpoints

| Endpoint | Method | Params | Notes |
|----------|--------|--------|-------|
| `/dmm/search` | POST | `{ "queryText": "..." }` | Unfiltered, 100 result cap |
| `/dmm/filtered` | GET | `query`, `season`, `episode`, `year`, `language`, `resolution`, `category`, `imdbId` | All params optional |
| `/imdb/search` | POST | `query`, `year`, `category` | IMDb file search |

### Search Internals

- Trigrams: `similarity("CleanedParsedTitle", query)` using GIN index
- `CleanedParsedTitle` = `Parsing.CleanQuery(ParsedTitle)` — removes stop words, collapses spaces
- DMM hashlist torrents go through `TorrentInfoService` which cleans titles
- Prowlarr torrents now use the same `CleanQuery` pipeline (as of commit `c387b94`)

---

## Prowlarr Backfill System

### Endpoints

| Endpoint | Method | Auth | Behavior |
|----------|--------|------|----------|
| `/admin/sources/backfill/{name}` | POST | API Key | Fire-and-forget, single source |
| `/admin/sources/backfill-all` | POST | API Key | Fire-and-forget, all enabled sources |
| `/dmm/on-demand-scrape` | GET | API Key | Fire-and-forget, DMM sync |
| `/admin/sources/trigger/{name}` | POST | API Key | Synchronous, waits for completion |
| `/admin/sources/status` | GET | API Key | Source stats (torrent counts, last sync) |

### Backfill Flow

```
POST /admin/sources/backfill/nyaa?untilDate=2025-01-01
  → Returns 200 immediately ("status": "running")
  → Background: Task.Run with IServiceScopeFactory
    → Reset LastSyncAt to untilDate
    → For each of 68 keywords (years, numbers, common words, letters):
      → Search Prowlarr with keyword: GET /{indexerId}/api?t=search&q={keyword}&offset=0&limit=100
      → Paginate results (5s between pages in backfill mode)
      → Upsert torrents into DB
      → 5s delay between keywords
    → If circuit breaker open → skip keyword, 10s delay, continue
    → Log completion: [ProwlarrBackfill] Complete for 'nyaa': N torrents from 68 keywords
```

### Keyword List (68 terms)
```
Years:    2000-2026 (27)
Numbers:  1-5 (5)
Words:    1080p, 2160p, BluRay, WEB-DL, HEVC, H264, x264, x265, the, and (10)
Letters:  a-z (26)
```

### Source Priority (Upsert Logic)

| Source | Priority |
|--------|----------|
| prowlarr | 5 |
| nyaa | 4 |
| yts | 3 |
| eztv | 2 |
| dmm | 1 |
| null (DMM hashlist) | 0 |

Higher priority sources overwrite lower ones on infoHash conflict.

### Prowlarr Rate Limits

- **Default**: 2 seconds between requests per indexer (built into Prowlarr)
- **Cloudflare**: `prowlarr.cloudpftc.com` adds Cloudflare rate limiting (429 + `Retry-After` header)
- **Indexer disabling**: Prowlarr disables indexers after repeated failures ("disabled till {date}")
- **Retry-After format**: Integer seconds (e.g., `retry-after: 81724`)

---

## Circuit Breaker & Resilience

### Standalone Polly Pipeline

The `ProwlarrSyncJob` uses a standalone Polly `ResiliencePipeline<HttpResponseMessage>` that bypasses the default `IHttpClientFactory` handlers (which chain a 30s timeout we can't override).

| Layer | Config | Purpose |
|-------|--------|---------|
| Rate Limiter | Token bucket: 1 token, 1 replen/3s, queue 10 | Prevents hitting 429 |
| Timeout | 5 minutes total | Accommodates backfill delays |
| Retry | 5 attempts, 3-30s exponential + jitter | Handles transient 429 |
| Circuit Breaker | 50% failures, 5+ attempts, 60s window, 30s break | Stops hammering |

### Behavior Log Example

```
[ProwlarrResilience] Retry 1/5 after 3.23s (429 rate limited)
[ProwlarrResilience] Retry 2/5 after 3.04s (429 rate limited)
[ProwlarrResilience] Circuit BREAKER OPEN — too many 429s, pausing 30s
[ProwlarrBackfill] Circuit breaker open, skipping keyword '2000' for 'nyaa'
```

### Key Principle

**Named `AddStandardResilienceHandler` on a named HttpClient CHAINS with the global default — it does NOT replace it.** Both handlers run, and the shortest timeout wins. This is why we use a standalone pipeline.

---

## Firebase-and-Forget Pattern

For endpoints that trigger long-running background work:

```csharp
private static IResult BackfillSource(..., IServiceScopeFactory scopeFactory)
{
    // Return immediately
    _ = Task.Run(async () =>
    {
        // Create a NEW scope — the HTTP request scope will be disposed
        await using var scope = scopeFactory.CreateAsyncScope();
        var syncJob = scope.ServiceProvider.GetRequiredService<ProwlarrSyncJob>();
        await syncJob.BackfillIndexerAsync(sourceName, date);
    });

    return TypedResults.Ok(new { message = "Backfill started in background", status = "running" });
}
```

**Rule**: Never pass scoped services (DbContext, sync jobs) directly into `Task.Run`. Always inject `IServiceScopeFactory` and create a new scope inside the background task.

---

## Log Analysis

### Log File Location
```
/mnt/nvme/comet/zilean/data/logs/zilean-YYYYMMDD.log
```

### Format
```
YYYY-MM-DD HH:MM:SS.fff +00:00 [LEVEL] Namespace.Class | Message
```

### Common Sources
- `ProwlarrSyncJob` / `ProwlarrBackfill` — Prowlarr sync/backfill
- `ProwlarrResilience` — Circuit breaker events
- `TorrentInfoService` — Torrent storage
- `Imdb` — IMDb matching
- `DmmSyncJob` — DMM scraping

### Common Queries
```bash
# List log files
ls -la /mnt/nvme/comet/zilean/data/logs/

# Today's log
TODAY=$(ls -t /mnt/nvme/comet/zilean/data/logs/ | head -1)

# Search for backfill activity
rg "ProwlarrBackfill" /mnt/nvme/comet/zilean/data/logs/$TODAY

# Check circuit breaker events
rg "ProwlarrResilience" /mnt/nvme/comet/zilean/data/logs/$TODAY

# Last N lines
tail -100 /mnt/nvme/comet/zilean/data/logs/$TODAY | grep -v "at " | grep -v "^---"
```

---

## Diagnostic Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/healthchecks/ping` | Liveness check |
| `/healthchecks/health` | DB health, indexes, extensions |
| `/diagnostics/stats` | Table row counts, sizes, DB size |
| `/diagnostics/freshness` | Torrent freshness by source |
| `/diagnostics/queue` | Ingestion queue status |
| `/diagnostics/misses` | Search miss tracking |
| `/diagnostics/cache` | Query cache stats |

---

## Database

### Key Tables
- `Torrents` — All torrent metadata (infoHash, titles, source, category)
- `TorrentSourceStats` — Per-source sync stats (last sync, torrent count)
- `IngestionQueue` — Pending ingestion items
- `ParsedPages` — DMM scrape tracking
- `FileAuditLogs` — Scrape operation history
- `QueryAudits` — Search query history

### Common Queries
```sql
-- Source distribution
SELECT "Source", COUNT(*) FROM "Torrents" GROUP BY "Source" ORDER BY COUNT(*) DESC;

-- Recent nyaa torrents
SELECT "ParsedTitle", "CleanedParsedTitle", "Category" FROM "Torrents"
WHERE "Source" = 'nyaa' ORDER BY "IngestedAt" DESC LIMIT 10;
```

---

## Deployment

### Local Testing
```bash
# Build image
docker compose -f docker-compose-test.yaml build zilean

# Start with Prowlarr API key
PROWLARR_API_KEY=xxx docker compose -f docker-compose-test.yaml up -d zilean

# View logs
docker logs zilean --tail 50
```

### Configuration
- `Zilean__Prowlarr__ApiKey` — Prowlarr API key (required for any Prowlarr operations)
- `Zilean__Prowlarr__Enabled` — Enable Prowlarr sync (cron + backfill endpoints)
- `Zilean__Prowlarr__BaseUrl` — Prowlarr instance URL
- `Zilean__Dmm__EnableScraping` / `Zilean__Dmm__EnableEndpoint` — DMM features

---

## Commit History (This Session)

```
73be8e7 test: add integration tests replicating Comet's search and backfill behavior
c387b94 fix(prowlarr): apply title cleaning and proper category mapping to backfilled torrents
6360e26 fix(backfill): skip keywords when circuit breaker is open instead of crashing
775a51c feat(prowlarr): standalone Polly resilience pipeline (rate limiter, retry, circuit breaker)
6d1b5be fix(on-demand-scrape): use IServiceScopeFactory to prevent ObjectDisposedException
6032bfc fix(backfill): use IServiceScopeFactory in fire-and-forget endpoints
```
