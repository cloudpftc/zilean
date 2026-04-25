using Microsoft.EntityFrameworkCore;
using Zilean.Shared.Features.Configuration;

namespace Zilean.ApiService.Features.Search;

public class BackgroundRefreshJob(
    ZileanDbContext dbContext,
    ZileanConfiguration configuration,
    ILogger<BackgroundRefreshJob> logger) : IInvocable
{
    public async Task Invoke()
    {
        var refreshSettings = configuration.Refresh;

        if (!refreshSettings.EnableRefreshOnMiss)
        {
            logger.LogDebug("Background refresh skipped - refresh on miss is disabled");
            return;
        }

        var staleThreshold = DateTime.UtcNow - refreshSettings.RefreshCooldown;
        var maxMiss = refreshSettings.MaxMissCountBeforeRefresh;

        var affected = await dbContext.Database
            .ExecuteSqlRawAsync(@"
                UPDATE ""Torrents""
                SET ""RefreshPending"" = false, ""LastRefreshedAt"" = NOW()
                WHERE ""RefreshPending"" = true
                AND (""MissCount"" >= {0} OR ""LastRefreshedAt"" < {1})",
                maxMiss,
                staleThreshold);

        if (affected > 0)
        {
            logger.LogInformation("Background refresh: processed {Count} torrents", affected);
        }
        else
        {
            logger.LogDebug("No torrents found requiring background refresh");
        }
    }
}