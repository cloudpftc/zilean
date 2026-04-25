using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Zilean.Shared.Features.Ingestion;

public class IngestionCheckpoint
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Required]
    [JsonPropertyName("source")]
    public string Source { get; set; } = default!;

    [JsonPropertyName("last_processed")]
    public string? LastProcessed { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Required]
    [JsonPropertyName("status")]
    public string Status { get; set; } = default!;

    [JsonPropertyName("items_processed")]
    public int ItemsProcessed { get; set; } = 0;
}
