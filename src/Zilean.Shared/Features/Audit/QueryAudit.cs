using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Zilean.Shared.Features.Audit;

public class QueryAudit
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Required]
    [JsonPropertyName("query")]
    public string Query { get; set; } = default!;

    [JsonPropertyName("filters_json")]
    public string? FiltersJson { get; set; }

    [JsonPropertyName("result_count")]
    public int ResultCount { get; set; } = 0;

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; set; } = 0;

    [JsonPropertyName("similarity_threshold")]
    public double? SimilarityThreshold { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("client_ip")]
    public string? ClientIp { get; set; }
}
