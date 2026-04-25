namespace Zilean.ApiService.Features.HealthChecks;

/// <summary>
/// Default implementation of <see cref="IHealthCheckService"/>.
/// </summary>
public class HealthCheckService : IHealthCheckService
{
    private readonly ZileanDbContext _dbContext;
    private readonly SyncOnDemandState _syncState;

    public HealthCheckService(ZileanDbContext dbContext, SyncOnDemandState syncState)
    {
        _dbContext = dbContext;
        _syncState = syncState;
    }

    /// <inheritdoc />
    public async Task<HealthStatus> CheckDatabaseAsync()
    {
        var now = DateTime.UtcNow;
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync();
            return new HealthStatus(
                Status: "database",
                IsHealthy: canConnect,
                Message: canConnect ? "Database connection successful" : "Database connection failed",
                CheckedAt: now);
        }
        catch (Exception ex)
        {
            return new HealthStatus(
                Status: "database",
                IsHealthy: false,
                Message: $"Database connection error: {ex.Message}",
                CheckedAt: now);
        }
    }

    /// <inheritdoc />
    public Task<HealthStatus> CheckIngestionStatus()
    {
        var now = DateTime.UtcNow;
        var isRunning = _syncState.IsRunning;

        return Task.FromResult(new HealthStatus(
            Status: "ingestion",
            IsHealthy: !isRunning,
            Message: isRunning ? "Ingestion is currently running" : "Ingestion is idle",
            CheckedAt: now));
    }

    /// <inheritdoc />
    public async Task<OverallHealth> GetOverallHealthAsync()
    {
        var checks = await Task.WhenAll(CheckDatabaseAsync(), CheckIngestionStatus());
        var isHealthy = checks.All(c => c.IsHealthy);

        return new OverallHealth(IsHealthy: isHealthy, Checks: checks);
    }
}
