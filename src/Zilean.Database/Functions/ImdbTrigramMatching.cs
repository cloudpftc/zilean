namespace Zilean.Database.Functions;

/// <summary>
/// PostgreSQL functions for batch IMDb matching using trigram similarity (pg_trgm).
/// </summary>
public static class ImdbTrigramMatching
{
    internal const string CreateMatchTitleFunction =
        """
        CREATE OR REPLACE FUNCTION imdb_match_title(p_torrent_title TEXT, p_imdb_title TEXT)
        RETURNS DOUBLE PRECISION AS $$
        BEGIN
            RETURN similarity(LOWER(p_torrent_title), LOWER(p_imdb_title));
        END;
        $$ LANGUAGE plpgsql IMMUTABLE;
        """;

    internal const string RemoveMatchTitleFunction =
        "DROP FUNCTION IF EXISTS imdb_match_title(TEXT, TEXT);";

    internal const string CreateBatchFindFunction =
        """
        CREATE OR REPLACE FUNCTION batch_find_imdb_matches(
            p_batch_size INT DEFAULT 1000,
            p_similarity_threshold DOUBLE PRECISION DEFAULT 0.45
        )
        RETURNS TABLE(info_hash TEXT, matched_imdb_id TEXT, match_score DOUBLE PRECISION) AS $$
        BEGIN
            RETURN QUERY
            SELECT DISTINCT ON (t."InfoHash")
                t."InfoHash",
                i."ImdbId" AS matched_imdb_id,
                imdb_match_title(t."CleanedParsedTitle", i."Title") AS match_score
            FROM public."Torrents" t
            CROSS JOIN LATERAL (
                SELECT "ImdbId", "Title"
                FROM public."ImdbFiles"
                WHERE imdb_match_title(t."CleanedParsedTitle", "Title") >= p_similarity_threshold
                ORDER BY imdb_match_title(t."CleanedParsedTitle", "Title") DESC
                LIMIT 1
            ) i
            WHERE t."ImdbId" IS NULL
              AND t."CleanedParsedTitle" <> ''
            ORDER BY t."InfoHash", match_score DESC
            LIMIT p_batch_size;
        END;
        $$ LANGUAGE plpgsql;
        """;

    internal const string RemoveBatchFindFunction =
        "DROP FUNCTION IF EXISTS batch_find_imdb_matches(INT, DOUBLE PRECISION);";

    internal const string CreateBatchUpdateFunction =
        """
        CREATE OR REPLACE FUNCTION batch_update_imdb_matches(
            p_batch_size INT DEFAULT 1000,
            p_similarity_threshold DOUBLE PRECISION DEFAULT 0.45
        )
        RETURNS INT AS $$
        DECLARE
            v_count INT;
        BEGIN
            WITH matches AS (
                SELECT DISTINCT ON (t."InfoHash")
                    t."InfoHash" AS torrent_hash,
                    i."ImdbId" AS matched_imdb_id,
                    imdb_match_title(t."CleanedParsedTitle", i."Title") AS match_score
                FROM public."Torrents" t
                CROSS JOIN LATERAL (
                    SELECT "ImdbId", "Title"
                    FROM public."ImdbFiles"
                    WHERE imdb_match_title(t."CleanedParsedTitle", "Title") >= p_similarity_threshold
                    ORDER BY imdb_match_title(t."CleanedParsedTitle", "Title") DESC
                    LIMIT 1
                ) i
                WHERE t."ImdbId" IS NULL
                  AND t."CleanedParsedTitle" <> ''
                ORDER BY t."InfoHash", match_score DESC
                LIMIT p_batch_size
            )
            UPDATE public."Torrents" t
            SET "ImdbId" = m.matched_imdb_id
            FROM matches m
            WHERE t."InfoHash" = m.torrent_hash;

            GET DIAGNOSTICS v_count = ROW_COUNT;
            RETURN v_count;
        END;
        $$ LANGUAGE plpgsql;
        """;

    internal const string RemoveBatchUpdateFunction =
        "DROP FUNCTION IF EXISTS batch_update_imdb_matches(INT, DOUBLE PRECISION);";
}
