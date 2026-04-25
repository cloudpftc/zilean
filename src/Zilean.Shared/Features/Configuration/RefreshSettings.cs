namespace Zilean.Shared.Features.Configuration;

public class RefreshSettings
{
    public bool EnableRefreshOnMiss { get; set; } = true;
    public int MaxMissCountBeforeRefresh { get; set; } = 5;
    public TimeSpan RefreshCooldown { get; set; } = TimeSpan.FromHours(24);
    public int MaxConcurrentRefreshes { get; set; } = 10;
}
