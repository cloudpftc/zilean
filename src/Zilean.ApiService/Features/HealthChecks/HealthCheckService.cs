using Dapper;
using Npgsql;

namespace Zilean.ApiService.Features.HealthChecks;

public class HealthCheckService : IHealthCheckService
{
    private readonly ZileanDbContext _dbContext;
    private readonly SyncOnDemandState _syncState;
    private readonly ZileanConfiguration _configuration;

    private static readonly string[] _requiredExtensions = ["pg_trgm", "unaccent", "btree_gin", "btree_gist"];
    private static readonly string[] _requiredIndexes = ["idx_cleaned_parsed_title_trgm", "idx_seasons_gin", "idx_episodes_gin", "idx_languages_gin"];

    public HealthCheckService(ZileanDbContext dbContext, SyncOnDemandState syncState, ZileanConfiguration configuration)
    {
        _dbContext = dbContext;
        _syncState = syncState;
        _configuration = configuration;
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
    public async Task<HealthStatus> CheckIndexesAsync()
    {
        var now = DateTime.UtcNow;
        try
        {
            await using var connection = new NpgsqlConnection(_configuration.Database.ConnectionString);
            await connection.OpenAsync();

            var missingExtensions = new List<string>();
            foreach (var ext in _requiredExtensions)
            {
                var exists = await connection.QuerySingleOrDefaultAsync<string>(
                    "SELECT extname FROM pg_extension WHERE extname = @ext",
                    new { ext });
                if (string.IsNullOrEmpty(exists))
                    missingExtensions.Add(ext);
            }

            var missingIndexes = new List<string>();
            foreach (var idx in _requiredIndexes)
            {
                var exists = await connection.QuerySingleOrDefaultAsync<string>(
                    "SELECT indexname FROM pg_indexes WHERE tablename = 'Torrents' AND indexname = @idx",
                    new { idx });
                if (string.IsNullOrEmpty(exists))
                    missingIndexes.Add(idx);
            }

            var issues = new List<string>();
            if (missingExtensions.Count > 0)
                issues.Add($"Missing extensions: {string.Join(", ", missingExtensions)}");
            if (missingIndexes.Count > 0)
                issues.Add($"Missing indexes: {string.Join(", ", missingIndexes)}");

            var isHealthy = missingExtensions.Count == 0 && missingIndexes.Count == 0;
            var message = isHealthy
                ? "All required extensions and indexes present"
                : string.Join("; ", issues);

            return new HealthStatus(Status: "indexes", IsHealthy: isHealthy, Message: message, CheckedAt: now);
        }
        catch (Exception ex)
        {
            return new HealthStatus(Status: "indexes", IsHealthy: false, Message: $"Index check error: {ex.Message}", CheckedAt: now);
        }
    }

    /// <inheritdoc />
    public async Task<OverallHealth> GetOverallHealthAsync()
    {
        var checks = await Task.WhenAll(CheckDatabaseAsync(), CheckIngestionStatus(), CheckIndexesAsync());
        var isHealthy = checks.All(c => c.IsHealthy);

        return new OverallHealth(IsHealthy: isHealthy, Checks: checks);
    }
}
