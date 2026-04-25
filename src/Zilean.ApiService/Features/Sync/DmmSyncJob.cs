using Zilean.ApiService.Features.Audit;

namespace Zilean.ApiService.Features.Sync;

public class DmmSyncJob(IShellExecutionService shellExecutionService, ILogger<DmmSyncJob> logger, ZileanDbContext dbContext, IFileAuditLogService fileAuditLogService) : IInvocable, ICancellableInvocable
{
    public CancellationToken CancellationToken { get; set; }
    private const string DmmSyncArg = "dmm-sync";

    public async Task Invoke()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            logger.LogInformation("Dmm SyncJob started");

            try
            {
                await fileAuditLogService.LogFileOperationAsync("scrape_start", null, "started", "DmmSyncJob", (int)sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to log scrape_start audit");
            }

            var argumentBuilder = ArgumentsBuilder.Create();
            argumentBuilder.AppendArgument(DmmSyncArg, string.Empty, false, false);

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
                await fileAuditLogService.LogFileOperationAsync("scrape_complete", null, "completed", "DmmSyncJob", (int)sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to log scrape_complete audit");
            }

            logger.LogInformation("Dmm SyncJob completed");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Dmm SyncJob failed");
            try
            {
                await fileAuditLogService.LogFileOperationAsync("file_error", null, "error", $"DmmSyncJob: {ex.Message}", (int)sw.ElapsedMilliseconds);
            }
            catch (Exception auditEx)
            {
                logger.LogWarning(auditEx, "Failed to log file_error audit");
            }
            throw;
        }
    }

    // ReSharper disable once MethodSupportsCancellation
    public Task<bool> ShouldRunOnStartup() => dbContext.ParsedPages.AnyAsync();
}
