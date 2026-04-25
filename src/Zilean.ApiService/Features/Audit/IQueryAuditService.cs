using Zilean.Shared.Features.Audit;

namespace Zilean.ApiService.Features.Audit;

/// <summary>
/// Provides query audit logging and retrieval operations.
/// </summary>
public interface IQueryAuditService
{
    /// <summary>
    /// Logs a search query to the audit table.
    /// </summary>
    /// <param name="query">The search query string.</param>
    /// <param name="endpoint">The API endpoint that handled the query.</param>
    /// <param name="clientIp">The client IP address, if available.</param>
    /// <param name="resultCount">Number of results returned.</param>
    /// <param name="durationMs">Query execution time in milliseconds.</param>
    /// <param name="timestamp">When the query was executed.</param>
    /// <param name="filtersJson">Optional JSON representation of applied filters.</param>
    /// <param name="similarityThreshold">Optional similarity threshold used for fuzzy matching.</param>
    Task LogQueryAsync(
        string query,
        string endpoint,
        string? clientIp,
        int resultCount,
        int durationMs,
        DateTime timestamp,
        string? filtersJson = null,
        double? similarityThreshold = null);

    /// <summary>
    /// Returns the most recent query audits.
    /// </summary>
    /// <param name="limit">Maximum number of records to return (default 100).</param>
    Task<IEnumerable<QueryAudit>> GetRecentQueriesAsync(int limit = 100);

    /// <summary>
    /// Returns query audits within a date range.
    /// </summary>
    Task<IEnumerable<QueryAudit>> GetQueriesByDateRangeAsync(DateTime start, DateTime end);

    /// <summary>
    /// Returns the most frequently searched queries with their counts.
    /// </summary>
    /// <param name="limit">Maximum number of top queries to return (default 20).</param>
    Task<Dictionary<string, int>> GetTopQueriesAsync(int limit = 20);
}
