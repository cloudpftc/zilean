using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zilean.Shared.Features.Audit;
using Zilean.Shared.Features.Configuration;

namespace Zilean.ApiService.Features.Audit;

public class QueryAuditService : IQueryAuditService
{
    private readonly ZileanDbContext _dbContext;
    private readonly ILogger<QueryAuditService> _logger;
    private readonly ZileanConfiguration _configuration;

    public QueryAuditService(ZileanDbContext dbContext, ILogger<QueryAuditService> logger, ZileanConfiguration configuration)
    {
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task LogQueryAsync(
        string query,
        string endpoint,
        string? clientIp,
        int resultCount,
        int durationMs,
        DateTime timestamp,
        string? filtersJson = null,
        double? similarityThreshold = null)
    {
        if (!_configuration.Audit.EnableQueryAuditing)
        {
            return;
        }

        var audit = new QueryAudit
        {
            Query = query,
            ClientIp = clientIp,
            ResultCount = resultCount,
            DurationMs = durationMs,
            Timestamp = timestamp,
            FiltersJson = filtersJson,
            SimilarityThreshold = similarityThreshold,
        };

        _dbContext.QueryAudits.Add(audit);
        await _dbContext.SaveChangesAsync();

        _logger.LogDebug("Logged query audit: {Query} -> {ResultCount} results in {DurationMs}ms", query, resultCount, durationMs);
    }

    public async Task<IEnumerable<QueryAudit>> GetRecentQueriesAsync(int limit = 100)
    {
        return await _dbContext.QueryAudits
            .OrderByDescending(q => q.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<QueryAudit>> GetQueriesByDateRangeAsync(DateTime start, DateTime end)
    {
        return await _dbContext.QueryAudits
            .Where(q => q.Timestamp >= start && q.Timestamp <= end)
            .OrderByDescending(q => q.Timestamp)
            .ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetTopQueriesAsync(int limit = 20)
    {
        return await _dbContext.QueryAudits
            .GroupBy(q => q.Query)
            .Select(g => new { Query = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToDictionaryAsync(x => x.Query, x => x.Count);
    }
}
