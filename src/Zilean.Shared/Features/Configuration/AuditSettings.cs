namespace Zilean.Shared.Features.Configuration;

public class AuditSettings
{
    public bool EnableQueryAuditing { get; set; } = true;
    public bool EnableFileAuditing { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public bool LogClientIp { get; set; } = true;
}
