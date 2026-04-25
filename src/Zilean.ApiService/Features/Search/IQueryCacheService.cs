using Zilean.Shared.Features.Dmm;

namespace Zilean.ApiService.Features.Search;

/// <summary>
/// Provides in-memory caching for search query results with TTL support.
/// </summary>
public interface IQueryCacheService
{
    /// <summary>
    /// Retrieves cached search results for the given cache key.
    /// </summary>
    /// <param name="cacheKey">The cache key derived from query parameters.</param>
    /// <returns>Cached torrent results, or null if not found.</returns>
    Task<TorrentInfo[]?> GetCachedAsync(string cacheKey);

    /// <summary>
    /// Stores search results in the cache with an optional TTL.
    /// </summary>
    /// <param name="cacheKey">The cache key derived from query parameters.</param>
    /// <param name="results">The search results to cache.</param>
    /// <param name="ttl">Optional time-to-live. Defaults to 5 minutes.</param>
    Task SetCachedAsync(string cacheKey, TorrentInfo[] results, TimeSpan? ttl = null);

    /// <summary>
    /// Removes a specific entry from the cache.
    /// </summary>
    /// <param name="cacheKey">The cache key to invalidate.</param>
    Task InvalidateAsync(string cacheKey);

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    Task InvalidateAllAsync();

    /// <summary>
    /// Returns current cache statistics.
    /// </summary>
    CacheStats GetStats();
}

/// <summary>
/// Statistics for the query cache.
/// </summary>
public record CacheStats(int Hits, int Misses, int Size);
