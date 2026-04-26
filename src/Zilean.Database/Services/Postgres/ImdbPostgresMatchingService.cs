#nullable disable
using Dapper;
using Npgsql;
using Zilean.Database.Services;

namespace Zilean.Database.Services.Postgres;

public class ImdbPostgresMatchingService(ILogger<ImdbPostgresMatchingService> logger, ZileanConfiguration configuration) : IImdbMatchingService
{
    public Task PopulateImdbData()
    {
        logger.LogInformation("PostgreSQL trigram matching service initialized - no in-memory cache needed");
        return Task.CompletedTask;
    }

    public void DisposeImdbData()
    {
    }

    public async Task<ConcurrentQueue<TorrentInfo>> MatchImdbIdsForBatchAsync(IEnumerable<TorrentInfo> batch)
    {
        var updatedTorrents = new ConcurrentQueue<TorrentInfo>();
        var torrentList = batch.ToList();

        if (torrentList.Count == 0)
        {
            return updatedTorrents;
        }

        logger.LogInformation("Starting PostgreSQL trigram matching for {Count} torrents", torrentList.Count);

        await using var connection = new NpgsqlConnection(configuration.Database.ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync("""
            CREATE TEMPORARY TABLE IF NOT EXISTS torrent_batch (
                info_hash TEXT PRIMARY KEY,
                cleaned_parsed_title TEXT,
                year INTEGER,
                category TEXT
            );
            TRUNCATE torrent_batch;
            """);

        var insertSql = """
            INSERT INTO torrent_batch (info_hash, cleaned_parsed_title, year, category)
            VALUES (@InfoHash, @CleanedParsedTitle, @Year, @Category)
            """;

        var batchParams = torrentList.Select(t => new
        {
            t.InfoHash,
            CleanedParsedTitle = t.CleanedParsedTitle ?? t.ParsedTitle ?? "",
            t.Year,
            t.Category,
        });

        await connection.ExecuteAsync(insertSql, batchParams);

        // Use a direct SQL query with DISTINCT ON instead of CROSS JOIN LATERAL
        // This processes all torrents in a single query instead of N separate function calls
        var matches = await connection.QueryAsync<(string InfoHash, string ImdbId, string MatchedTitle, int MatchedYear, float Score)>(
            """
            SELECT DISTINCT ON (tb.info_hash)
                tb.info_hash,
                i."ImdbId" AS imdb_id,
                i."Title" AS matched_title,
                i."Year" AS matched_year,
                word_similarity(tb.cleaned_parsed_title, i."Title") AS score
            FROM torrent_batch tb
            CROSS JOIN LATERAL (
                SELECT "ImdbId", "Title", "Year"
                FROM public."ImdbFiles"
                WHERE "Title" % tb.cleaned_parsed_title
                  AND word_similarity(tb.cleaned_parsed_title, "Title") > 0.45
                  AND (tb.year IS NULL OR "Year" = 0 OR ABS("Year" - tb.year) <= 1)
                  AND (tb.category IS NULL OR
                       (tb.category = 'movie' AND "Category" IN ('movie', 'tvMovie')) OR
                       (tb.category = 'tvSeries' AND "Category" IN ('tvSeries', 'tvMiniSeries', 'tvShort', 'tvSpecial')))
                ORDER BY word_similarity(tb.cleaned_parsed_title, "Title") DESC
                LIMIT 1
            ) i
            """);

        var matchList = matches.ToList();
        logger.LogInformation("PostgreSQL trigram matching found {MatchCount} matches", matchList.Count);

        var torrentByInfoHash = torrentList.ToDictionary(t => t.InfoHash, t => t);

        foreach (var match in matchList)
        {
            if (torrentByInfoHash.TryGetValue(match.InfoHash, out var torrent))
            {
                var oldImdbId = torrent.ImdbId;
                torrent.ImdbId = match.ImdbId;

                logger.LogInformation(
                    "Torrent '{Title}' updated from IMDb ID '{OldImdbId}' to '{NewImdbId}' with a score of {Score}, Category: {Category}, Imdb Title: {ImdbTitle}, Imdb Year: {ImdbYear}",
                    torrent.ParsedTitle, oldImdbId, match.ImdbId, match.Score, torrent.Category, match.MatchedTitle, match.MatchedYear);

                updatedTorrents.Enqueue(torrent);
            }
        }

        var matchedInfoHashes = matchList.Select(m => m.InfoHash).ToHashSet();
        foreach (var torrent in torrentList)
        {
            if (!matchedInfoHashes.Contains(torrent.InfoHash))
            {
                logger.LogWarning(
                    "No suitable match found for Torrent '{Title}', Category: {Category}",
                    torrent.ParsedTitle, torrent.Category);
            }
        }

        logger.LogInformation("PostgreSQL trigram matching completed. Updated {UpdatedCount} torrents", updatedTorrents.Count);
        return updatedTorrents;
    }
}
