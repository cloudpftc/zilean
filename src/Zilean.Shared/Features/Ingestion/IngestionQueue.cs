using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Zilean.Shared.Features.Ingestion;

public class IngestionQueue
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Required]
    [JsonPropertyName("info_hash")]
    public string InfoHash { get; set; } = default!;

    [Required]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; } = 0;
}
