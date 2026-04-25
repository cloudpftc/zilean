namespace Zilean.Shared.Features.Configuration;

public class PersistenceSettings
{
    public int MaxRetryCount { get; set; } = 3;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public bool EnableSensitiveDataLogging { get; set; } = false;
    public int BulkInsertBatchSize { get; set; } = 1000;

    // Synchronous commit mode for PostgreSQL (options: "on", "off", "local")
    public string SynchronousCommitMode { get; set; } = "on";

    // Connection pooling parameters
    public int MaxPoolSize { get; set; } = 100;
    public int MinPoolSize { get; set; } = 5;
    public int ConnectionTimeout { get; set; } = 30;

    // Checkpoint retention
    public int CheckpointRetentionDays { get; set; } = 30;
}
