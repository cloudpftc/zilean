namespace Zilean.Shared.Features.Configuration;

public class DiagnosticsSettings
{
    public bool EnableHealthChecks { get; set; } = true;
    public int HealthCheckIntervalSeconds { get; set; } = 60;
    public bool EnablePerformanceCounters { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
}
