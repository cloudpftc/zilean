using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Zilean.Shared.Features.Audit;

public class FileAuditLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Required]
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = default!;

    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [Required]
    [JsonPropertyName("status")]
    public string Status { get; set; } = default!;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; set; } = 0;
}
