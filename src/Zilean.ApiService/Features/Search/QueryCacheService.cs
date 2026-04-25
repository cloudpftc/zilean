using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Zilean.Shared.Features.Dmm;

namespace Zilean.ApiService.Features.Search;

public class QueryCacheService : IQueryCacheService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache;
    private readonly ILogger<QueryCacheService> _logger;
    private int _hits;
    private int _misses;

    public QueryCacheService(IMemoryCache cache, ILogger<QueryCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<TorrentInfo[]?> GetCachedAsync(string cacheKey)
    {
        if (_cache.TryGetValue(cacheKey, out TorrentInfo[]? result))
        {
            Interlocked.Increment(ref _hits);
            _logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);
            return Task.FromResult(result);
        }

        Interlocked.Increment(ref _misses);
        _logger.LogDebug("Cache miss for key: {CacheKey}", cacheKey);
        return Task.FromResult<TorrentInfo[]?>(null);
    }

    public Task SetCachedAsync(string cacheKey, TorrentInfo[] results, TimeSpan? ttl = null)
    {
        var effectiveTtl = ttl ?? DefaultTtl;
        _cache.Set(cacheKey, results, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = effectiveTtl,
            Size = 1,
        });

        _logger.LogDebug("Cached {Count} results for key: {CacheKey} (TTL: {Ttl})", results.Length, cacheKey, effectiveTtl);
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string cacheKey)
    {
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated cache key: {CacheKey}", cacheKey);
        return Task.CompletedTask;
    }

    public Task InvalidateAllAsync()
    {
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Compact(1.0);
            _logger.LogDebug("Invalidated all cache entries");
        }

        return Task.CompletedTask;
    }

    public CacheStats GetStats()
    {
        var size = _cache is MemoryCache memoryCache
            ? memoryCache.Count
            : 0;

        return new CacheStats(
            Volatile.Read(ref _hits),
            Volatile.Read(ref _misses),
            size);
    }
}
