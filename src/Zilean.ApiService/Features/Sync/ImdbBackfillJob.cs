namespace Zilean.ApiService.Features.Sync;

/// <summary>
/// Schedulable entry point for the IMDb-driven Prowlarr backfill.
///
/// This exists purely so <see cref="ProwlarrSyncJob.BackfillFromImdbTitlesAsync"/>
/// can be driven by Coravel on a cron without turning a failed run into an
/// unhandled exception that could take down the scheduler (and thus the whole
/// ApiService). Exceptions are caught and logged here; the scheduled sync and
/// this backfill also share the <c>SyncJobs</c> overlap key so they can never
/// run at the same time.
///
/// The job refuses to do anything unless
/// <see cref="ProwlarrConfiguration.ImdbBackfillEnabled"/> is true.
/// </summary>
public class ImdbBackfillJob(
    ILogger<ImdbBackfillJob> logger,
    ProwlarrSyncJob syncJob,
    ZileanConfiguration configuration) : IInvocable, ICancellableInvocable
{
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (!configuration.Prowlarr.ImdbBackfillEnabled)
            {
                logger.LogInformation("[ImdbBackfill] Job disabled (Prowlarr.ImdbBackfillEnabled=false), skipping");
                return;
            }

            logger.LogInformation("[ImdbBackfill] Scheduled job starting");
            var count = await syncJob.BackfillFromImdbTitlesAsync();
            sw.Stop();

            logger.LogInformation("[ImdbBackfill] Scheduled job finished: {Count} torrents upserted in {ElapsedMs}ms",
                count, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[ImdbBackfill] Scheduled job failed after {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
    }
}
