namespace Zilean.ApiService.Features.Search;

/// <summary>
/// Provides miss tracking for search queries with zero results.
/// </summary>
public interface IMissTrackingService
{
    /// <summary>
    /// Tracks a search query that returned zero results.
    /// Increments MissCount on matching torrents or creates a new miss record.
    /// </summary>
    /// <param name="query">The search query that returned no results.</param>
    /// <param name="category">Optional category filter that was applied.</param>
    Task TrackMissAsync(string query, string? category);

    /// <summary>
    /// Returns the top missed search queries by miss count.
    /// </summary>
    /// <param name="limit">Maximum number of records to return (default 20).</param>
    Task<IEnumerable<MissRecord>> GetTopMissesAsync(int limit = 20);

    /// <summary>
    /// Marks a query as refreshed, clearing its miss tracking state.
    /// </summary>
    /// <param name="query">The query that has been refreshed.</param>
    Task MarkRefreshedAsync(string query);
}

/// <summary>
/// Represents a recorded miss for a search query.
/// </summary>
public record MissRecord(string Query, string? Category, int MissCount, DateTime LastMissed);
