namespace Zilean.ApiService.Features.HealthChecks;

/// <summary>
/// Provides health check operations for the Zilean API service.
/// </summary>
public interface IHealthCheckService
{
    /// <summary>
    /// Checks database connectivity by executing a simple query against <see cref="ZileanDbContext"/>.
    /// </summary>
    /// <returns>A <see cref="HealthStatus"/> indicating whether the database is reachable.</returns>
    Task<HealthStatus> CheckDatabaseAsync();

    /// <summary>
    /// Checks whether the on-demand ingestion/sync process is currently running.
    /// </summary>
    /// <returns>A <see cref="HealthStatus"/> indicating the ingestion subsystem state.</returns>
    Task<HealthStatus> CheckIngestionStatus();

    /// <summary>
    /// Aggregates all individual health checks into a single overall health report.
    /// </summary>
    /// <returns>An <see cref="OverallHealth"/> summarising the health of all subsystems.</returns>
    Task<OverallHealth> GetOverallHealthAsync();
}

/// <summary>
/// Represents the result of a single health check.
/// </summary>
public record HealthStatus(string Status, bool IsHealthy, string Message, DateTime CheckedAt);

/// <summary>
/// Represents the aggregated health of all checked subsystems.
/// </summary>
public record OverallHealth(bool IsHealthy, HealthStatus[] Checks);
