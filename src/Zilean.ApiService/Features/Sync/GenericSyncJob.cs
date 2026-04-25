using Zilean.ApiService.Features.Audit;
using Zilean.ApiService.Features.Ingestion;

namespace Zilean.ApiService.Features.Sync;

public class GenericSyncJob(IShellExecutionService shellExecutionService, ILogger<GenericSyncJob> logger, ZileanDbContext dbContext, IFileAuditLogService fileAuditLogService, IIngestionQueueService ingestionQueueService) : IInvocable, ICancellableInvocable
{
    public CancellationToken CancellationToken { get; set; }
    private const string GenericSyncArg = "generic-sync";

    public async Task Invoke()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            logger.LogInformation("Generic SyncJob started");

            try
            {
                await fileAuditLogService.LogFileOperationAsync("scrape_start", null, "started", "GenericSyncJob", (int)sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to log scrape_start audit");
            }

            // Dequeue and process pending queue items before running scraper
            await ProcessQueueItemsAsync();

            var argumentBuilder = ArgumentsBuilder.Create();
            argumentBuilder.AppendArgument(GenericSyncArg, string.Empty, false, false);

            await shellExecutionService.ExecuteCommand(new ShellCommandOptions
            {
                Command = Path.Combine(AppContext.BaseDirectory, "scraper"),
                ArgumentsBuilder = argumentBuilder,
                ShowOutput = true,
                CancellationToken = CancellationToken
            });

            sw.Stop();
            try
            {
                await fileAuditLogService.LogFileOperationAsync("scrape_complete", null, "completed", "GenericSyncJob", (int)sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to log scrape_complete audit");
            }

            logger.LogInformation("Generic SyncJob completed");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Generic SyncJob failed");
            try
            {
                await fileAuditLogService.LogFileOperationAsync("file_error", null, "error", $"GenericSyncJob: {ex.Message}", (int)sw.ElapsedMilliseconds);
            }
            catch (Exception auditEx)
            {
                logger.LogWarning(auditEx, "Failed to log file_error audit");
            }
            throw;
        }
    }

    /// <summary>
    /// Dequeues pending queue items and marks them as processed.
    /// Queue failures are caught and logged but do not break the job.
    /// </summary>
    private async Task ProcessQueueItemsAsync()
    {
        try
        {
            var processedIds = new List<int>();

            while (true)
            {
                var item = await ingestionQueueService.DequeueAsync();
                if (item == null)
                {
                    break;
                }

                processedIds.Add(item.Id);
                logger.LogDebug("Dequeued queue item {Id} with info hash {InfoHash}", item.Id, item.InfoHash);
            }

            // Mark all dequeued items as processed
            foreach (var id in processedIds)
            {
                try
                {
                    await ingestionQueueService.MarkProcessedAsync(id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to mark queue item {Id} as processed", id);
                }
            }

            if (processedIds.Count > 0)
            {
                logger.LogInformation("Processed {Count} pending queue items", processedIds.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Queue processing failed - continuing with sync job");
        }
    }

    // ReSharper disable once MethodSupportsCancellation
    public Task<bool> ShouldRunOnStartup() => dbContext.ParsedPages.AnyAsync();
}
