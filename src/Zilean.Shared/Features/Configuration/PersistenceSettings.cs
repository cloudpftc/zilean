namespace Zilean.Shared.Features.Configuration;

public class PersistenceSettings
{
    public int MaxRetryCount { get; set; } = 3;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public bool EnableSensitiveDataLogging { get; set; } = false;
    public int BulkInsertBatchSize { get; set; } = 1000;
}
