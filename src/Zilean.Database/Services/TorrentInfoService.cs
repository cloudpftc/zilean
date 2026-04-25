using Zilean.Shared.Features.Ingestion;

namespace Zilean.Database.Services;

public class TorrentInfoService(ILogger<TorrentInfoService> logger, ZileanConfiguration configuration, IServiceProvider serviceProvider, IIngestionCheckpointService? checkpointService = null)
    : BaseDapperService(logger, configuration), ITorrentInfoService
{
    public async Task VaccumTorrentsIndexes(CancellationToken cancellationToken)
    {
        await using var serviceScope = serviceProvider.CreateAsyncScope();
        await using var dbContext = serviceScope.ServiceProvider.GetRequiredService<ZileanDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync("VACUUM (VERBOSE, ANALYZE) \"Torrents\"", cancellationToken: cancellationToken);
    }

    public async Task StoreTorrentInfo(List<TorrentInfo> torrents, int batchSize = 10000, string? source = null)
    {
        if (torrents.Count == 0)
        {
            logger.LogInformation("No torrents to store.");
            return;
        }

        // Dynamic batch size based on torrent count
        var baseBatchSize = Configuration.Persistence.BulkInsertBatchSize;
        var effectiveBatchSize = batchSize;
        if (torrents.Count > 50000)
        {
            effectiveBatchSize = Math.Min(baseBatchSize * 2, 5000);
        }
        if (torrents.Count > 100000)
        {
            effectiveBatchSize = Math.Min(baseBatchSize * 3, 10000);
        }
        logger.LogInformation("Using batch size {BatchSize} for {Count} torrents", effectiveBatchSize, torrents.Count);

        foreach (var torrentInfo in torrents)
        {
            torrentInfo.CleanedParsedTitle = Parsing.CleanQuery(torrentInfo.ParsedTitle);
        }

        await using var serviceScope = serviceProvider.CreateAsyncScope();
        await using var dbContext = serviceScope.ServiceProvider.GetRequiredService<ZileanDbContext>();
        await using var connection = new NpgsqlConnection(Configuration.Database.ConnectionString);
        var imdbMatchingService = serviceScope.ServiceProvider.GetRequiredService<IImdbMatchingService>();

        await imdbMatchingService.PopulateImdbData();

        var bulkConfig = new BulkConfig
        {
            SetOutputIdentity = false,
            BatchSize = effectiveBatchSize,
            PropertiesToIncludeOnUpdate = [string.Empty],
            UpdateByProperties = ["InfoHash"],
            BulkCopyTimeout = 0,
            TrackingEntities = false,
        };

        dbContext.Database.SetCommandTimeout(0);

        var chunks = torrents.Chunk(effectiveBatchSize).ToList();

        logger.LogInformation("Storing {Count} torrents in {BatchSize} batches", torrents.Count, chunks.Count);
        var currentBatch = 0;
        var totalProcessed = 0;
        var ingestionStart = DateTime.UtcNow;
        foreach (var batch in chunks)
        {
            currentBatch++;

            if (Configuration.Imdb.EnableImportMatching)
            {
                logger.LogInformation("Matching IMDb IDs for batch {CurrentBatch} of {TotalBatches}", currentBatch, chunks.Count);
                await imdbMatchingService.MatchImdbIdsForBatchAsync(batch);
            }

            logger.LogInformation("Storing batch {CurrentBatch} of {TotalBatches}", currentBatch, chunks.Count);
            await dbContext.BulkInsertOrUpdateAsync(batch, bulkConfig);

            totalProcessed += batch.Length;

            var elapsed = DateTime.UtcNow - ingestionStart;
            var itemsPerSecond = elapsed.TotalSeconds > 0 ? totalProcessed / elapsed.TotalSeconds : 0;
            var remaining = torrents.Count - totalProcessed;
            var etaSeconds = itemsPerSecond > 0 ? remaining / itemsPerSecond : 0;
            var eta = TimeSpan.FromSeconds(etaSeconds);
            logger.LogInformation("Ingestion progress: {TotalProcessed}/{TotalCount} ({Rate:F1} items/sec) - ETA: {Eta:hh\\:mm\\:ss}", totalProcessed, torrents.Count, itemsPerSecond, eta);

            if (checkpointService is not null && !string.IsNullOrEmpty(source))
            {
                try
                {
                    var lastInfoHash = batch[^1].InfoHash;
                    await checkpointService.SaveCheckpointAsync(source, lastInfoHash, "in_progress", totalProcessed);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to save ingestion checkpoint for source {Source}", source);
                }
            }
        }

        if (checkpointService is not null && !string.IsNullOrEmpty(source))
        {
            try
            {
                var lastInfoHash = chunks[^1][^1].InfoHash;
                await checkpointService.SaveCheckpointAsync(source, lastInfoHash, "completed", totalProcessed);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to save completion checkpoint for source {Source}", source);
            }
        }

        imdbMatchingService.DisposeImdbData();
    }

    public async Task<TorrentInfo[]> SearchForTorrentInfoByOnlyTitle(string query)
    {
        var cleanQuery = Parsing.CleanQuery(query);
        var animeCategoryBoost = Configuration.Dmm.AnimeCategoryBoost;
        var animeCompleteBoost = Configuration.Dmm.AnimeCompleteSeriesBoost;

        return await ExecuteCommandAsync(async connection =>
        {
            var sql =
                """
                SELECT
                    *,
                    CASE
                        WHEN ("Category" ILIKE '%anime%' OR "Category" ILIKE '%TVAnime%')
                             AND "Complete" = true
                        THEN similarity("CleanedParsedTitle", @query) * @animeCompleteBoost
                        WHEN "Category" ILIKE '%anime%' OR "Category" ILIKE '%TVAnime%'
                        THEN similarity("CleanedParsedTitle", @query) * @animeCategoryBoost
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
            parameters.Add("@animeCategoryBoost", animeCategoryBoost);
            parameters.Add("@animeCompleteBoost", animeCompleteBoost);

            var result = await connection.QueryAsync<TorrentInfo>(sql, parameters);

            return result.ToArray();
        }, "Error finding unfiltered dmm entries.");
    }

    public async Task<TorrentInfo[]> SearchForTorrentInfoFiltered(TorrentInfoFilter filter, int? limit = null)
    {
        var cleanQuery = Parsing.CleanQuery(filter.Query);
        var imdbId = EnsureCorrectFormatImdbId(filter);
        var audioPreference = Configuration.Dmm.AnimeAudioPreference;

        return await ExecuteCommandAsync(async connection =>
        {
            const string sql =
                """
                   SELECT *
                   FROM search_torrents_meta(
                       @Query,
                       @Season,
                       @Episode,
                       @Year,
                       @Language,
                       @Resolution,
                       @ImdbId,
                       @Limit,
                       @Category,
                       @SimilarityThreshold
                   );
                 """;

            var parameters = new DynamicParameters();

            parameters.Add("@Query", cleanQuery);
            parameters.Add("@Season", filter.Season);
            parameters.Add("@Episode", filter.Episode);
            parameters.Add("@Year", filter.Year);
            parameters.Add("@Language", filter.Language);
            parameters.Add("@Resolution", filter.Resolution);
            parameters.Add("@Category", filter.Category);
            parameters.Add("@ImdbId", imdbId);
            parameters.Add("@Limit", limit ?? Configuration.Dmm.MaxFilteredResults);
            parameters.Add("@SimilarityThreshold", (float)Configuration.Dmm.MinimumScoreMatch);

            var results = await connection.QueryAsync<TorrentInfoResult>(sql, parameters);

            var torrentResults = results.Select(MapImdbDataToTorrentInfo).ToArray();

            if (audioPreference != "any")
            {
                var audioBoost = 1.2;
                torrentResults = torrentResults
                    .Select(t => new { Torrent = t, Boost = CalculateAudioBoost(t, audioPreference, audioBoost) })
                    .OrderByDescending(x => x.Boost)
                    .Select(x => x.Torrent)
                    .ToArray();
            }

            return torrentResults;
        }, "Error finding unfiltered dmm entries.");
    }

    private static double CalculateAudioBoost(TorrentInfo torrent, string preference, double boostFactor)
    {
        var isAnime = torrent.Category?.Contains("anime", StringComparison.OrdinalIgnoreCase) == true ||
                      torrent.Category?.Contains("TVAnime", StringComparison.OrdinalIgnoreCase) == true;

        if (!isAnime)
            return 1.0;

        return preference switch
        {
            "subbed" when torrent.Subbed == true => boostFactor,
            "dubbed" when torrent.Dubbed == true => boostFactor,
            _ => 1.0
        };
    }

    private static string? EnsureCorrectFormatImdbId(TorrentInfoFilter filter)
    {
        string? imdbId = null;
        if (!string.IsNullOrEmpty(filter.ImdbId))
        {
            imdbId = filter.ImdbId.StartsWith("tt") ? filter.ImdbId : $"tt{filter.ImdbId}";
        }

        return imdbId;
    }

    private static Func<TorrentInfoResult, TorrentInfoResult> MapImdbDataToTorrentInfo =>
        torrentInfo =>
        {
            if (torrentInfo.ImdbId != null)
            {
                torrentInfo.Imdb = new()
                {
                    ImdbId = torrentInfo.ImdbId,
                    Category = torrentInfo.ImdbCategory,
                    Title = torrentInfo.ImdbTitle,
                    Year = torrentInfo.ImdbYear ?? 0,
                    Adult = torrentInfo.ImdbAdult,
                };
            }

            return torrentInfo;
        };

    public async Task<HashSet<string>> GetExistingInfoHashesAsync(List<string> infoHashes)
    {
        await using var serviceScope = serviceProvider.CreateAsyncScope();
        await using var dbContext = serviceScope.ServiceProvider.GetRequiredService<ZileanDbContext>();

        var existingHashes = await dbContext.Torrents
            .Where(t => infoHashes.Contains(t.InfoHash))
            .Select(t => t.InfoHash)
            .ToListAsync();

        return [..existingHashes];
    }

    public async Task<HashSet<string>> GetBlacklistedItems()
    {
        await using var serviceScope = serviceProvider.CreateAsyncScope();
        await using var dbContext = serviceScope.ServiceProvider.GetRequiredService<ZileanDbContext>();

        var existingHashes = await dbContext.BlacklistedItems
            .Select(t => t.InfoHash)
            .ToListAsync();

        return [..existingHashes];
    }
}
