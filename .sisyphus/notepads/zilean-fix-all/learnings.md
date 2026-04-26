# zilean-fix-all Notepad

## Root Cause Analysis

### Task 5: Scraper Ingestion Fix

**Problem**: Torrents table had 0 rows after scraper ran successfully.

**Root Causes Found**:

1. **Initial Issue**: CROSS JOIN LATERAL calling `match_torrents_to_imdb` function was EXTREMELY slow
   - The function used `word_similarity > 0.85` without efficient index usage
   - Each call took 6+ seconds, making 5000 torrents = 8+ hours

2. **Fix Applied**: Changed function to use `%` operator (GIN index) + lower threshold 0.45
   - Reduced per-call time from 6s to 200-400ms

3. **Remaining Issue**: CROSS JOIN LATERAL still calls function N times
   - 5000 torrents * 200ms = 1000 seconds = ~17 minutes
   - This blocked the scraper pipeline

4. **Temporary Fix**: Set `EnableImportMatching=false` to store torrents without matching
   - Result: 49,567 torrents stored successfully

5. **Current Fix**: Reduced `MaxBatchSize` from 50000 to 1000
   - 1000 torrents * 200ms = ~3-4 minutes per batch
   - More manageable for scraper pipeline

## Key Files Modified

- `src/Zilean.Database/Migrations/20260425130000_BatchImdbMatchFunction.cs` - Fixed threshold 0.85 → 0.45, added `%` operator
- `src/Zilean.Database/Services/Postgres/ImdbPostgresMatchingService.cs` - Changed query to use DISTINCT ON
- `docker-compose-test.yaml` - MaxBatchSize=1000, EnableImportMatching=true

## PostgreSQL Functions

- `match_torrents_to_imdb(p_title, p_year, p_category)` - Single row matching
- `batch_match_torrents_to_imdb()` - Batch matching (created but not yet used)

## GIN Index on ImdbFiles

Created: `idx_imdbfiles_title_trgm` on `public."ImdbFiles"."Title"` with `gin_trgm_ops`

## Test Commands

```bash
# Test matching function
docker exec zilean-db psql -U postgres -d zilean -c "SELECT * FROM match_torrents_to_imdb('Breaking Bad', 2008, 'tvSeries');"

# Check active queries
docker exec zilean-db psql -U postgres -d zilean -c "SELECT pid, state, query_start FROM pg_stat_activity WHERE state != 'idle';"

# Cancel stuck query
docker exec zilean-db psql -U postgres -d zilean -c "SELECT pg_cancel_backend(pid);"
```
