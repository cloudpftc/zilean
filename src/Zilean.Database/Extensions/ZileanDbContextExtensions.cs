using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using Zilean.Shared.Features.Dmm;

namespace Zilean.Database.Extensions;

public static class ZileanDbContextExtensions
{
    private static readonly Dictionary<string, int> _sourcePriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prowlarr"] = 5,
        ["nyaa"] = 4,
        ["yts"] = 3,
        ["eztv"] = 2,
        ["dmm"] = 1,
    };

    private static readonly string _upsertSql = BuildUpsertSql();

    private sealed record ColumnDef(string JsonKey, string DbColumn, string PgType);

    private static readonly ColumnDef[] _columnDefinitions =
    [
        new("info_hash", "InfoHash", "text"),
        new("raw_title", "RawTitle", "text"),
        new("parsed_title", "ParsedTitle", "text"),
        new("normalized_title", "NormalizedTitle", "text"),
        new("cleaned_parsed_title", "CleanedParsedTitle", "text"),
        new("trash", "Trash", "boolean"),
        new("year", "Year", "integer"),
        new("resolution", "Resolution", "text"),
        new("seasons", "Seasons", "integer[]"),
        new("episodes", "Episodes", "integer[]"),
        new("complete", "Complete", "boolean"),
        new("volumes", "Volumes", "integer[]"),
        new("languages", "Languages", "text[]"),
        new("quality", "Quality", "text"),
        new("hdr", "Hdr", "text[]"),
        new("codec", "Codec", "text"),
        new("audio", "Audio", "text[]"),
        new("channels", "Channels", "text[]"),
        new("dubbed", "Dubbed", "boolean"),
        new("subbed", "Subbed", "boolean"),
        new("date", "Date", "text"),
        new("group", "Group", "text"),
        new("edition", "Edition", "text"),
        new("bit_depth", "BitDepth", "text"),
        new("bitrate", "Bitrate", "text"),
        new("network", "Network", "text"),
        new("extended", "Extended", "boolean"),
        new("converted", "Converted", "boolean"),
        new("hardcoded", "Hardcoded", "boolean"),
        new("region", "Region", "text"),
        new("ppv", "Ppv", "boolean"),
        new("_3d", "Is3d", "boolean"),
        new("site", "Site", "text"),
        new("size", "Size", "text"),
        new("proper", "Proper", "boolean"),
        new("repack", "Repack", "boolean"),
        new("retail", "Retail", "boolean"),
        new("upscaled", "Upscaled", "boolean"),
        new("remastered", "Remastered", "boolean"),
        new("unrated", "Unrated", "boolean"),
        new("documentary", "Documentary", "boolean"),
        new("episode_code", "EpisodeCode", "text"),
        new("country", "Country", "text"),
        new("container", "Container", "text"),
        new("extension", "Extension", "text"),
        new("torrent", "Torrent", "boolean"),
        new("imdb_id", "ImdbId", "text"),
        new("adult", "IsAdult", "boolean"),
        new("ingested_at", "IngestedAt", "timestamp with time zone"),
        new("last_refreshed_at", "LastRefreshedAt", "timestamp with time zone"),
        new("miss_count", "MissCount", "integer"),
        new("refresh_pending", "RefreshPending", "boolean"),
        new("source", "Source", "text"),
    ];

    public static async Task UpsertTorrentsAsync(
        this ZileanDbContext db,
        IEnumerable<TorrentInfo> torrents,
        string source,
        CancellationToken ct = default)
    {
        var sourcePriority = GetSourcePriority(source);

        foreach (var batch in torrents.Chunk(1000))
        {
            await UpsertBatchAsync(db, batch, sourcePriority, ct);
        }
    }

    private static async Task UpsertBatchAsync(
        ZileanDbContext db,
        TorrentInfo[] batch,
        int sourcePriority,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(batch);
        var parameters = new List<object>
        {
            new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = json },
            new NpgsqlParameter("source_priority", sourcePriority),
        };

        await db.Database.ExecuteSqlRawAsync(_upsertSql, parameters, ct);
    }

    private static int GetSourcePriority(string source) =>
        _sourcePriorities.GetValueOrDefault(source, 0);

    private static string BuildUpsertSql()
    {
        var insertColumns = string.Join(", ", _columnDefinitions.Select(c => $@"""{c.DbColumn}"""));
        var jsonColumns = string.Join(", ", _columnDefinitions.Select(c => $"{c.JsonKey} {c.PgType}"));
        var selectColumns = string.Join(", ", _columnDefinitions.Select(c => $"x.{c.JsonKey} AS \"{c.DbColumn}\""));
        var updateSetColumns = string.Join(", ", _columnDefinitions
            .Where(c => c.DbColumn != "InfoHash")
            .Select(c => $@"""{c.DbColumn}"" = EXCLUDED.""{c.DbColumn}"""));

        return $$"""
            INSERT INTO "Torrents" ({{insertColumns}})
            SELECT {{selectColumns}}
            FROM jsonb_to_recordset(@data::jsonb) AS x({{jsonColumns}})
            ON CONFLICT ("InfoHash") DO UPDATE SET
                {{updateSetColumns}}
            WHERE (
                "Torrents"."Source" IS NULL
                OR @source_priority >= CASE "Torrents"."Source"
                    WHEN 'prowlarr' THEN 5
                    WHEN 'nyaa' THEN 4
                    WHEN 'yts' THEN 3
                    WHEN 'eztv' THEN 2
                    WHEN 'dmm' THEN 1
                    ELSE 0
                END
            )
            """;
    }
}
