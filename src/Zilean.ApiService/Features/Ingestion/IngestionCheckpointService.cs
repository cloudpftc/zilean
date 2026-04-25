using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zilean.Shared.Features.Ingestion;

namespace Zilean.ApiService.Features.Ingestion;

public class IngestionCheckpointService : IIngestionCheckpointService
{
    private readonly ZileanDbContext _dbContext;
    private readonly ILogger<IngestionCheckpointService> _logger;

    public IngestionCheckpointService(ZileanDbContext dbContext, ILogger<IngestionCheckpointService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IngestionCheckpoint?> LoadCheckpointAsync(string source)
    {
        return await _dbContext.IngestionCheckpoints
            .FirstOrDefaultAsync(c => c.Source == source);
    }

    public async Task SaveCheckpointAsync(string source, string lastProcessed, string status, int itemsProcessed)
    {
        var checkpoint = await _dbContext.IngestionCheckpoints
            .FirstOrDefaultAsync(c => c.Source == source);

        if (checkpoint == null)
        {
            checkpoint = new IngestionCheckpoint
            {
                Source = source,
                LastProcessed = lastProcessed,
                Status = status,
                ItemsProcessed = itemsProcessed,
                Timestamp = DateTime.UtcNow,
            };
            _dbContext.IngestionCheckpoints.Add(checkpoint);
        }
        else
        {
            checkpoint.LastProcessed = lastProcessed;
            checkpoint.Status = status;
            checkpoint.ItemsProcessed = itemsProcessed;
            checkpoint.Timestamp = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogDebug("Saved ingestion checkpoint for source {Source}: status={Status}, items={ItemsProcessed}",
            source, status, itemsProcessed);
    }

    public async Task DeleteCheckpointAsync(string source)
    {
        var checkpoint = await _dbContext.IngestionCheckpoints
            .FirstOrDefaultAsync(c => c.Source == source);

        if (checkpoint != null)
        {
            _dbContext.IngestionCheckpoints.Remove(checkpoint);
            await _dbContext.SaveChangesAsync();

            _logger.LogDebug("Deleted ingestion checkpoint for source {Source}", source);
        }
    }
}
