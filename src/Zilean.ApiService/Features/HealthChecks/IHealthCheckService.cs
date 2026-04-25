namespace Zilean.ApiService.Features.HealthChecks;

public interface IHealthCheckService
{
    Task<HealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);
}

public record HealthStatus(
    string Service,
    string Status,
    DateTime Timestamp,
    string? Details = null
);
