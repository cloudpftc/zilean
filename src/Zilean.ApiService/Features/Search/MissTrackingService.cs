using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zilean.Shared.Features.Dmm;

namespace Zilean.ApiService.Features.Search;

public class MissTrackingService : IMissTrackingService
{
    private readonly ZileanDbContext _dbContext;
    private readonly ILogger<MissTrackingService> _logger;

    public MissTrackingService(ZileanDbContext dbContext, ILogger<MissTrackingService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task TrackMissAsync(string query, string? category)
    {
        var normalizedQuery = query.ToLowerInvariant().Trim();

        var torrents = await _dbContext.Torrents
            .Where(t => t.NormalizedTitle != null && t.NormalizedTitle.Contains(normalizedQuery))
            .ToListAsync();

        if (torrents.Count > 0)
        {
            foreach (var torrent in torrents)
            {
                torrent.MissCount++;
                torrent.RefreshPending = true;
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogDebug("Tracked miss for query '{Query}' on {Count} torrent(s)", query, torrents.Count);
        }
        else
        {
            _logger.LogDebug("No matching torrent found for missed query '{Query}'", query);
        }
    }

    public async Task<IEnumerable<MissRecord>> GetTopMissesAsync(int limit = 20)
    {
        return await _dbContext.Torrents
            .Where(t => t.MissCount > 0)
            .OrderByDescending(t => t.MissCount)
            .Take(limit)
            .Select(t => new MissRecord(
                t.NormalizedTitle ?? t.RawTitle ?? t.InfoHash,
                t.Category,
                t.MissCount,
                t.LastRefreshedAt ?? t.IngestedAt))
            .ToListAsync();
    }

    public async Task MarkRefreshedAsync(string query)
    {
        var normalizedQuery = query.ToLowerInvariant().Trim();

        var affected = await _dbContext.Torrents
            .Where(t => t.NormalizedTitle != null && t.NormalizedTitle.Contains(normalizedQuery))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.RefreshPending, false)
                .SetProperty(t => t.LastRefreshedAt, DateTime.UtcNow));

        _logger.LogDebug("Marked {Count} torrent(s) as refreshed for query '{Query}'", affected, query);
    }
}
