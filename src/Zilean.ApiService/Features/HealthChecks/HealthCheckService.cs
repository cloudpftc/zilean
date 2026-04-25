namespace Zilean.ApiService.Features.HealthChecks;

public class HealthCheckService : IHealthCheckService
{
    public Task<HealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new HealthStatus(
            Service: "Zilean.ApiService",
            Status: "Healthy",
            Timestamp: DateTime.UtcNow
        ));
    }
}
