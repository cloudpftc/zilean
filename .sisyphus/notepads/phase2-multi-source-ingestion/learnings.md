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
