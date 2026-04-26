namespace Zilean.Shared.Features.Configuration;

public class ProwlarrConfiguration
{
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Cron { get; set; } = "0 */6 * * *";
    public List<ProwlarrIndexer> Indexers { get; set; } = [];
}

public class ProwlarrIndexer
{
    public int IndexerId { get; set; }
    public string SourceName { get; set; } = "";
    public string Categories { get; set; } = "2000,5000";
    public bool Enabled { get; set; } = false;
}
