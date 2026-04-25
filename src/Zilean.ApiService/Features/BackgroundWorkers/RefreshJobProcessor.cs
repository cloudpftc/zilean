namespace Zilean.ApiService.Features.BackgroundWorkers;

/// <summary>
/// Background worker that processes refresh jobs from the queue.
/// Runs continuously with bounded concurrency for low-RAM safety.
/// </summary>
public class RefreshJobProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RefreshJobProcessor> _logger;
    private readonly IOptions<AggressivePersistenceOptions> _options;
    private readonly SemaphoreSlim _concurrencySemaphore;

    public RefreshJobProcessor(
        IServiceProvider serviceProvider,
        ILogger<RefreshJobProcessor> logger,
        IOptions<AggressivePersistenceOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;
        _concurrencySemaphore = new SemaphoreSlim(options.Value.MaxConcurrentRefreshJobs, options.Value.MaxConcurrentRefreshJobs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Refresh job processor starting with max concurrency={MaxConcurrency}", 
            _options.Value.MaxConcurrentRefreshJobs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRefreshJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in refresh job processor loop");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // Backoff on error
            }
        }

        _logger.LogInformation("Refresh job processor shutting down");
    }

    private async Task ProcessRefreshJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var refreshJobService = scope.ServiceProvider.GetRequiredService<RefreshJobService>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<AuditLogger>();

        // Get pending jobs
        var pendingJobs = await refreshJobService.GetPendingJobsAsync(cancellationToken);

        if (pendingJobs.Count == 0)
        {
            // No jobs to process, wait before checking again
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return;
        }

        _logger.LogDebug("Found {Count} pending refresh jobs", pendingJobs.Count);

        // Process jobs with bounded concurrency
        var tasks = pendingJobs.Select(job => ProcessSingleJobAsync(job, refreshJobService, auditLogger, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task ProcessSingleJobAsync(
        RefreshJob job,
        RefreshJobService refreshJobService,
        AuditLogger auditLogger,
        CancellationToken cancellationToken)
    {
        await _concurrencySemaphore.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Processing refresh job: {Query} (trigger={Trigger})", 
                job.NormalizedQuery, job.TriggerType);

            await refreshJobService.MarkJobProcessingAsync(job, cancellationToken);
            await auditLogger.WriteRefreshJobAsync(job, cancellationToken);

            // TODO: Implement actual refresh logic based on job type
            // For now, this is a placeholder that will be connected to DMM/generic scraping
            int entriesAdded = 0;

            // Simulate work based on trigger type
            switch (job.TriggerType)
            {
                case RefreshJobTrigger.QueryMiss:
                    // Trigger targeted scrape for this query
                    entriesAdded = await ExecuteQueryMissRefreshAsync(job, cancellationToken);
                    break;
                case RefreshJobTrigger.Scheduled:
                    // Run scheduled incremental sync
                    entriesAdded = await ExecuteScheduledRefreshAsync(job, cancellationToken);
                    break;
                case RefreshJobTrigger.Manual:
                    // Manual refresh requested
                    entriesAdded = await ExecuteManualRefreshAsync(job, cancellationToken);
                    break;
            }

            await refreshJobService.MarkJobCompletedAsync(job, entriesAdded, cancellationToken);
            await auditLogger.WriteRefreshJobAsync(job, cancellationToken);

            _logger.LogInformation("Refresh job completed: {Query}, entriesAdded={Entries}", 
                job.NormalizedQuery, entriesAdded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing refresh job: {Query}", job.NormalizedQuery);
            await refreshJobService.MarkJobFailedAsync(job, ex.Message, cancellationToken);
            await auditLogger.WriteFailureAsync("refresh-processor", "RefreshJobProcessor", ex, cancellationToken);
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private async Task<int> ExecuteQueryMissRefreshAsync(RefreshJob job, CancellationToken cancellationToken)
    {
        // Placeholder: In production, this would trigger targeted scraping
        // based on the query fingerprint and normalized query
        _logger.LogDebug("Executing query-miss refresh for: {Query}", job.NormalizedQuery);
        
        // For now, just simulate some work
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        
        return 0; // No entries added yet (placeholder)
    }

    private async Task<int> ExecuteScheduledRefreshAsync(RefreshJob job, CancellationToken cancellationToken)
    {
        // Placeholder: In production, this would run incremental sync
        _logger.LogDebug("Executing scheduled refresh");
        
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        
        return 0;
    }

    private async Task<int> ExecuteManualRefreshAsync(RefreshJob job, CancellationToken cancellationToken)
    {
        // Placeholder: In production, this would trigger full or targeted refresh
        _logger.LogDebug("Executing manual refresh");
        
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        
        return 0;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Refresh job processor stopping...");
        await base.StopAsync(cancellationToken);
        _concurrencySemaphore.Dispose();
    }
}
