using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zilean.Database.Migrations;

/// <inheritdoc />
public partial class BatchImdbMatchFunction : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION match_torrents_to_imdb(
                p_title TEXT,
                p_year INTEGER DEFAULT NULL,
                p_category TEXT DEFAULT NULL
            ) RETURNS TABLE(imdb_id TEXT, matched_title TEXT, matched_year INTEGER, score DOUBLE PRECISION) AS $$
            BEGIN
                RETURN QUERY
                SELECT "ImdbId", "Title", "Year",
                       word_similarity(p_title, "Title") AS score
                FROM public."ImdbFiles"
                WHERE word_similarity(p_title, "Title") > 0.85
                  AND (p_year IS NULL OR "Year" = 0 OR ABS("Year" - p_year) <= 1)
                  AND (p_category IS NULL OR
                       (p_category = 'movie' AND "Category" IN ('movie', 'tvMovie')) OR
                       (p_category = 'tvSeries' AND "Category" IN ('tvSeries', 'tvMiniSeries', 'tvShort', 'tvSpecial')))
                ORDER BY score DESC
                LIMIT 1;
            END;
            $$ LANGUAGE plpgsql;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS match_torrents_to_imdb(TEXT, INTEGER, TEXT);");
    }
}
