using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Zilean.Shared.Features.Ingestion;

public class TorrentSourceStats
{
    [Key]
    [JsonPropertyName("source")]
    public string Source { get; set; } = default!;

    [JsonPropertyName("last_sync_at")]
    public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("torrent_count")]
    public long TorrentCount { get; set; } = 0;

    [JsonPropertyName("last_error")]
    public string? LastError { get; set; }
}
