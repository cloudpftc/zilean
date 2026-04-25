namespace Zilean.Shared.Features.Audit;

/// <summary>
/// Structured audit logger for append-only JSONL logging.
/// Thread-safe and designed for Docker volume mounts.
/// </summary>
public class AuditLogger : IDisposable
{
    private readonly string _auditDirectory;
    private readonly bool _prettyPrint;
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
    private readonly Dictionary<string, StreamWriter> _writers = new();
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(IOptions<AggressivePersistenceOptions> options, ILogger<AuditLogger> logger)
    {
        var opts = options.Value;
        _auditDirectory = opts.AuditDirectory;
        _prettyPrint = opts.AuditJsonPretty;
        _logger = logger;

        // Ensure directory exists
        Directory.CreateDirectory(_auditDirectory);
        _logger.LogInformation("Audit logger initialized at {Directory}", _auditDirectory);
    }

    /// <summary>
    /// Write an audit event to the specified log file.
    /// </summary>
    public async Task WriteAsync(string logType, AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_auditDirectory))
        {
            Directory.CreateDirectory(_auditDirectory);
        }

        var filePath = Path.Combine(_auditDirectory, $"{logType}.jsonl");
        
        await _writeSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!_writers.TryGetValue(logType, out var writer))
            {
                var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan | FileOptions.Asynchronous);
                writer = new StreamWriter(stream, Encoding.UTF8);
                _writers[logType] = writer;
                _logger.LogDebug("Opened audit log file: {FilePath}", filePath);
            }

            var jsonOptions = _prettyPrint 
                ? new JsonSerializerOptions { WriteIndented = true } 
                : new JsonSerializerOptions { WriteIndented = false };
            
            var json = JsonSerializer.Serialize(auditEvent, jsonOptions);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit event to {LogType}", logType);
            throw;
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Write ingestion run event.
    /// </summary>
    public Task WriteIngestionRunAsync(SourceSyncRun syncRun, CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent
        {
            Timestamp = DateTime.UtcNow,
            Level = syncRun.Status == SyncStatus.Failed ? "Error" : "Info",
            EventType = "ingestion_run",
            Subsystem = "ingestion",
            Source = syncRun.SourceType.ToString(),
            Status = syncRun.Status.ToString().ToLowerInvariant(),
            ElapsedMs = syncRun.EndTime.HasValue ? (long)(syncRun.EndTime.Value - syncRun.StartTime).TotalMilliseconds : null,
            Summary = $"DMM sync completed: {syncRun.EntriesAdded} added, {syncRun.PagesProcessed} pages",
            Details = new
            {
                syncRun.Id,
                syncRun.SourceType,
                syncRun.Status,
                syncRun.StartTime,
                syncRun.EndTime,
                syncRun.PagesProcessed,
                syncRun.EntriesAdded,
                syncRun.EntriesUpdated,
                syncRun.ErrorSummary,
                syncRun.Retries
            }
        };
        
        return WriteAsync("ingestion-runs", auditEvent, cancellationToken);
    }

    /// <summary>
    /// Write search query audit event.
    /// </summary>
    public Task WriteSearchQueryAsync(QueryAudit queryAudit, CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent
        {
            Timestamp = queryAudit.CreatedAt,
            Level = "Info",
            EventType = "search_query",
            Subsystem = "search",
            Source = "api",
            Status = queryAudit.ResultsReturned > 0 ? "success" : "empty",
            ElapsedMs = null,
            Summary = $"Search: {queryAudit.RawQuery} -> {queryAudit.ResultsReturned} results",
            CorrelationId = queryAudit.CorrelationId,
            Details = new
            {
                queryAudit.Id,
                queryAudit.RawQuery,
                queryAudit.NormalizedQuery,
                queryAudit.ContentType,
                queryAudit.ParsedSeason,
                queryAudit.ParsedEpisode,
                queryAudit.CandidateCount,
                queryAudit.ResultsReturned,
                queryAudit.TopConfidence,
                queryAudit.TriggeredRefresh,
                queryAudit.SourcesConsulted
            }
        };
        
        return WriteAsync("search-queries", auditEvent, cancellationToken);
    }

    /// <summary>
    /// Write query miss event.
    /// </summary>
    public Task WriteQueryMissAsync(QueryMiss queryMiss, CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent
        {
            Timestamp = queryMiss.LastSeen,
            Level = "Warning",
            EventType = "search_miss",
            Subsystem = "search",
            Source = "api",
            Status = queryMiss.RefreshTriggered ? "refresh_scheduled" : "no_refresh",
            ElapsedMs = null,
            Summary = $"Query miss: {queryMiss.NormalizedQueryFingerprint}",
            Details = new
            {
                queryMiss.Id,
                queryMiss.NormalizedQueryFingerprint,
                queryMiss.RawQueryExamples,
                queryMiss.MissCount,
                queryMiss.ContentHints,
                queryMiss.RefreshTriggered,
                queryMiss.LastSeen
            }
        };
        
        return WriteAsync("search-misses", auditEvent, cancellationToken);
    }

    /// <summary>
    /// Write refresh job event.
    /// </summary>
    public Task WriteRefreshJobAsync(RefreshJob refreshJob, CancellationToken cancellationToken = default)
    {
        var level = refreshJob.Status switch
        {
            RefreshJobStatus.Completed => "Info",
            RefreshJobStatus.Failed => "Error",
            RefreshJobStatus.Processing => "Info",
            _ => "Info"
        };

        var auditEvent = new AuditEvent
        {
            Timestamp = refreshJob.CreatedAt,
            Level = level,
            EventType = "refresh_job",
            Subsystem = "ingestion",
            Source = refreshJob.TriggerType.ToString().ToLowerInvariant(),
            Status = refreshJob.Status.ToString().ToLowerInvariant(),
            ElapsedMs = refreshJob.StartedAt.HasValue && refreshJob.CompletedAt.HasValue 
                ? (long)(refreshJob.CompletedAt.Value - refreshJob.StartedAt.Value).TotalMilliseconds 
                : null,
            Summary = $"Refresh job: {refreshJob.NormalizedQuery} -> {refreshJob.Status}",
            Details = new
            {
                refreshJob.Id,
                refreshJob.TriggerType,
                refreshJob.QueryFingerprint,
                refreshJob.NormalizedQuery,
                refreshJob.Status,
                refreshJob.CreatedAt,
                refreshJob.StartedAt,
                refreshJob.CompletedAt,
                refreshJob.EntriesAdded,
                refreshJob.ErrorSummary,
                refreshJob.RetryCount
            }
        };
        
        return WriteAsync("refresh-jobs", auditEvent, cancellationToken);
    }

    /// <summary>
    /// Write ranking debug event.
    /// </summary>
    public Task WriteRankingDebugAsync(object rankingDetails, CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent
        {
            Timestamp = DateTime.UtcNow,
            Level = "Debug",
            EventType = "ranking_debug",
            Subsystem = "search",
            Source = "ranking",
            Status = "debug",
            ElapsedMs = null,
            Summary = "Ranking breakdown",
            Details = rankingDetails
        };
        
        return WriteAsync("ranking-debug", auditEvent, cancellationToken);
    }

    /// <summary>
    /// Write failure/exception event.
    /// </summary>
    public Task WriteFailureAsync(string subsystem, string source, Exception exception, CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent
        {
            Timestamp = DateTime.UtcNow,
            Level = "Error",
            EventType = "failure",
            Subsystem = subsystem,
            Source = source,
            Status = "error",
            ElapsedMs = null,
            Summary = exception.Message,
            Details = new
            {
                exception.GetType().Name,
                exception.Message,
                exception.StackTrace,
                InnerException = exception.InnerException?.Message
            }
        };
        
        return WriteAsync("failures", auditEvent, cancellationToken);
    }

    /// <summary>
    /// Write freshness/scheduler event.
    /// </summary>
    public Task WriteFreshnessAsync(string sourceType, object freshnessData, CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent
        {
            Timestamp = DateTime.UtcNow,
            Level = "Info",
            EventType = "freshness",
            Subsystem = "scheduler",
            Source = sourceType,
            Status = "info",
            ElapsedMs = null,
            Summary = $"Freshness check for {sourceType}",
            Details = freshnessData
        };
        
        return WriteAsync("freshness", auditEvent, cancellationToken);
    }

    public void Dispose()
    {
        _writeSemaphore.Wait();
        try
        {
            foreach (var writer in _writers.Values)
            {
                writer.Dispose();
            }
            _writers.Clear();
        }
        finally
        {
            _writeSemaphore.Release();
            _writeSemaphore.Dispose();
        }
    }
}

/// <summary>
/// Base audit event structure for JSONL logging.
/// </summary>
public class AuditEvent
{
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = "Info";
    public string EventType { get; init; } = "unknown";
    public string Subsystem { get; init; } = "unknown";
    public string Source { get; init; } = "unknown";
    public string Status { get; init; } = "unknown";
    public long? ElapsedMs { get; init; }
    public string Summary { get; init; } = "";
    public string? CorrelationId { get; init; }
    public object? Details { get; init; }
}
