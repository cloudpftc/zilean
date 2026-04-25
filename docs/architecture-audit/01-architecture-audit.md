# Zilean Architecture Audit

**Date**: 2026-04-25
**Scope**: Full codebase audit for Postgres-first, low-RAM, incrementally improving search service transformation
**Status**: Complete

---

## 1. Project Overview

Zilean is a .NET 9 search service for DebridMediaManager (DMM) content. It scrapes torrent metadata, ingests it into PostgreSQL, and provides Torznab-compatible search API endpoints for media management tools (Radarr, Sonarr, etc.).

### 1.1 Project Structure

| Project | Purpose | Key Dependencies |
|---------|---------|-----------------|
| `Zilean.ApiService` | Web API (port 8181), Coravel scheduling, Blazor dashboard | Coravel, Syncfusion.Blazor, pythonnet |
| `Zilean.Database` | EF Core + Dapper data access, Lucene/FuzzySharp IMDb matching | EFCore.BulkExtensions, Lucene.NET, FuzzySharp, Npgsql |
| `Zilean.Shared` | Domain models, configuration, Python interop, scraping utilities | pythonnet3, System.Text.Json |
| `Zilean.Scraper` | CLI scraper executable | Dapper, Npgsql |
| `Zilean.Benchmarks` | Performance benchmarks | BenchmarkDotNet |

### 1.2 Tech Stack

- **Runtime**: .NET 9
- **Database**: PostgreSQL 17 (Npgsql provider)
- **ORM**: EF Core + Dapper (dual approach)
- **Full-text search**: Lucene.NET (in-memory) + PostgreSQL pg_trgm
- **Fuzzy matching**: FuzzySharp (Levenshtein distance)
- **Scheduling**: Coravel (invocable jobs)
- **Python interop**: pythonnet (Python 3.11 embedded for torrent name parsing)
- **Dashboard**: Syncfusion Blazor
- **Bulk operations**: EFCore.BulkExtensions

---

## 2. Database Architecture

### 2.1 Entity Models (5 entities)

#### TorrentInfo (Primary entity, ~50 properties)
- **PK**: `InfoHash` (text, 40-char hex)
- **Key fields**: RawTitle, ParsedTitle, NormalizedTitle, CleanedParsedTitle, Year, Resolution, Seasons (int[]), Episodes (int[]), Languages (text[]), Quality, Codec, Audio (text[]), HDR (text[]), Category, ImdbId, IsAdult, IngestedAt
- **Relationships**: FK to ImdbFile via ImdbId

#### ImdbFile
- **PK**: `ImdbId` (text, format "tt0000000")
- **Fields**: Title, Year, Category, Adult

#### ParsedPages
- Tracks scraped pages for incremental scraping

#### ImportMetadata
- Statistics about imports (counts, timestamps)

#### BlacklistedItem
- **PK**: InfoHash - items excluded from search results

### 2.2 Index Strategy

| Index | Column | Method | Purpose |
|-------|--------|--------|---------|
| `idx_cleaned_parsed_title_trgm` | CleanedParsedTitle | GIN (gin_trgm_ops) | Trigram fuzzy search |
| `idx_seasons_gin` | Seasons (int[]) | GIN | Season array containment |
| `idx_episodes_gin` | Episodes (int[]) | GIN | Episode array containment |
| `idx_languages_gin` | Languages (text[]) | GIN | Language array containment |
| `idx_year` | Year | B-tree | Year filtering |
| `idx_torrents_imdbid` | ImdbId | B-tree | IMDb join |
| `idx_torrents_isadult` | IsAdult | B-tree | Adult filtering |
| `idx_torrents_trash` | Trash | B-tree | Trash filtering |
| `idx_ingested_at` | IngestedAt | B-tree (DESC) | Recency ordering |
| PK index | InfoHash | B-tree (unique) | Primary key |

### 2.3 Search Function (V5 - Current)

`search_torrents_meta()` - PostgreSQL plpgsql function with 10 parameters:
- `query TEXT` - search query
- `season INT`, `episode INT` - episode filtering
- `year INT` - year filter (±1 year tolerance)
- `language TEXT`, `resolution TEXT`, `category TEXT` - exact match filters
- `imdbId TEXT` - direct IMDb lookup
- `limit_param INT DEFAULT 20` - result limit
- `similarity_threshold REAL DEFAULT 0.85` - trigram similarity cutoff

**Ranking**: `similarity(t."CleanedParsedTitle", query)` as Score, ordered by Score DESC, IngestedAt DESC.

**JOIN**: LEFT JOIN with ImdbFiles for metadata enrichment.

### 2.4 Migrations

21 migrations total, showing evolution from basic release table to sophisticated search. Key milestones:
- Initial release table → Functions and indexes → V2-V5 search functions → Trigram indexes → IMDb integration → Unaccent extension

---

## 3. Search Implementation

### 3.1 Search Flow

```
API Request → SearchEndpoints.cs → TorrentInfoService → Dapper → PostgreSQL
```

**Two search modes**:
1. **Unfiltered** (`SearchForTorrentInfoByOnlyTitle`): Direct Dapper query using `ParsedTitle % @query` trigram operator, LIMIT 100
2. **Filtered** (`SearchForTorrentInfoFiltered`): Calls `search_torrents_meta()` stored function with all filter parameters

### 3.2 Query Normalization

`Parsing.CleanQuery()`:
- Removes stop words: a, the, and, of, in, on, with, to, for, by, is, it
- Normalizes whitespace (multiple spaces → single space)
- Applied to both stored titles (CleanedParsedTitle) and incoming queries

### 3.3 IMDb Matching (Two implementations)

#### Lucene-based (ImdbLuceneMatchingService)
- Loads ALL ImdbFiles from DB into in-memory Lucene index per batch
- FuzzyQuery on title + TermQuery on category + NumericRangeQuery on year (±1)
- Parallel matching grouped by year+category
- ConcurrentDictionary cache for repeated lookups
- **RAM concern**: Loads entire IMDb dataset into memory per batch

#### FuzzySharp-based (ImdbFuzzyStringMatchingService)
- Year-partitioned ConcurrentDictionary
- Score calculation: ExactMatch=2.0, CloseMatch=1.5, Fuzzy=0-1.0
- **RAM concern**: Full IMDb data in memory

---

## 4. Ingestion Architecture

### 4.1 Sync Jobs

**DmmSyncJob** and **GenericSyncJob** (Coravel-scheduled invocables):
- Both execute external `scraper` binary via `ShellExecutionService`
- `ShouldRunOnStartup()`: checks if ParsedPages exist
- No checkpoint/resume logic
- No progress tracking
- No error recovery

### 4.2 Storage Pipeline

```
Scraper → ShellExecutionService → TorrentInfoService.StoreTorrentInfo()
```

**StoreTorrentInfo flow**:
1. Clean all titles (Parsing.CleanQuery)
2. Load IMDb data into Lucene index (`PopulateImdbData`)
3. Chunk torrents into batches of 5000
4. For each batch:
   - Match IMDb IDs (if enabled)
   - Bulk insert via EFCore.BulkExtensions
5. Dispose IMDb data

### 4.3 Duplicate Detection

`GetExistingInfoHashesAsync()`: EF Core query checking which infohashes already exist. Called before ingestion to filter duplicates.

### 4.4 Critical Issues

| Issue | Severity | Impact |
|-------|----------|--------|
| No checkpoint/resume | HIGH | Full re-ingestion on failure |
| All torrents loaded into memory before batching | HIGH | RAM spikes with large datasets |
| Lucene index rebuilt per batch | HIGH | CPU + RAM waste |
| No ingestion queue table | MEDIUM | No visibility into pending work |
| No freshness tracking | MEDIUM | Cannot detect stale data |
| No refresh-on-miss | MEDIUM | Missing results never auto-corrected |

---

## 5. API Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/search` | POST | Unfiltered search by title |
| `/filtered` | GET | Filtered search with all parameters |
| `/on-demand-scrape` | GET (auth) | Manual scrape trigger |
| `/ping` | GET | Health check (returns "Pong!" + timestamp) |
| Torznab endpoints | GET/POST | Indexer-compatible API |
| Blacklist endpoints | CRUD | Manage blacklisted items |
| IMDb endpoints | CRUD | Manage IMDb data |

### 5.1 Health Check Gap

Current `/ping` endpoint only returns timestamp. No checks for:
- Database connectivity
- Ingestion status
- Index health
- Queue depth
- Data freshness

---

## 6. Configuration

`ZileanConfiguration` sub-configs:
- **Dmm**: EnableScraping, MaxFilteredResults, MinimumScoreMatch
- **Torznab**: Indexer settings
- **Database**: ConnectionString, requires POSTGRES_PASSWORD env var
- **Torrents**: Torrent-related settings
- **Imdb**: EnableImportMatching, UseAllCores, NumberOfCores
- **Ingestion**: Ingestion settings
- **Parsing**: Parsing settings

**Missing configs needed**:
- Aggressive persistence settings
- RAM limits
- Checkpoint intervals
- Refresh-on-miss thresholds
- Query telemetry toggle

---

## 7. Python Interop

`ParseTorrentNameService` uses pythonnet to embed Python 3.11 for torrent name parsing. This is a significant RAM and startup cost.

---

## 8. Caching

**Current**: In-memory ConcurrentDictionary only. No IMemoryCache, no IDistributedCache, no Redis.

**Implications**: 
- IMDb data reloaded per ingestion batch
- No query result caching
- No cross-instance state sharing

---

## 9. Anime Handling

TorznabCategoryTypes defines `TVAnime (5070)` as a subcategory of TV. No special search logic, ranking boost, or complete-series preference for anime content.

---

## 10. Pain Points Summary

| # | Pain Point | Phase | Severity |
|---|-----------|-------|----------|
| 1 | Lucene.NET adds RAM pressure | Phase 2 | HIGH |
| 2 | pythonnet embedded Python heavy | Phase 3 | HIGH |
| 3 | Bulk ingestion loads all into memory | Phase 3 | HIGH |
| 4 | No resumable ingestion checkpoints | Phase 3 | HIGH |
| 5 | No query telemetry/audit | Phase 5 | MEDIUM |
| 6 | No refresh-on-miss logic | Phase 4 | MEDIUM |
| 7 | No anime-specific handling | Phase 7 | LOW |
| 8 | No structured file audit logs | Phase 5 | MEDIUM |
| 9 | No diagnostic endpoints | Phase 6 | MEDIUM |
| 10 | Health check too minimal | Phase 6 | MEDIUM |
| 11 | No aggressive persistence config | Phase 2 | MEDIUM |
| 12 | IMDb matching rebuilds index per batch | Phase 3 | HIGH |

---

## 11. Recommendations

### Phase 2: Postgres-First Search
- Remove Lucene.NET dependency entirely
- Move IMDb matching to PostgreSQL (trigram matching on ImdbFiles table)
- Add aggressive persistence: synchronous commits during ingestion
- Consider materialized views for common query patterns

### Phase 3: Low-RAM Ingestion
- Stream-based ingestion: process torrents one-by-one or in small batches
- Replace Lucene with PostgreSQL-based IMDb matching
- Add checkpoint table: `IngestionCheckpoints (id, source, last_processed, timestamp, status)`
- Add ingestion queue table: `IngestionQueue (id, infohash, status, created_at, processed_at)`
- Evaluate pythonnet replacement with native .NET parsing

### Phase 4: Incremental Improvement
- Add refresh-on-miss: when search returns 0 results, trigger background scrape for that query
- Track miss counts per query pattern
- Auto-refresh stale entries (older than configurable threshold)

### Phase 5: Audit Trails
- Add `QueryAudit` table: (id, query, filters, result_count, duration_ms, timestamp, similarity_threshold)
- Add `FileAuditLog` table: (id, operation, file_path, status, timestamp, details)
- Add telemetry toggle in configuration

### Phase 6: Diagnostic Endpoints
- `/health` - full health check (DB, ingestion, indexes)
- `/diagnostics/freshness` - data age statistics
- `/diagnostics/queue` - ingestion queue status
- `/diagnostics/misses` - top missed queries
- `/diagnostics/stats` - database statistics

### Phase 7: Polish
- Anime-specific ranking (boost complete series, prefer subbed/dubbed)
- Query result caching (Redis or in-memory with TTL)
- Advanced trigram tuning per category
