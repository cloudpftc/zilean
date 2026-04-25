namespace Zilean.Scraper.Features.Ingestion.Checkpointing;

/// <summary>
/// Service for managing ingestion checkpoints in PostgreSQL.
/// Enables resumable ingestion by persisting progress state.
/// </summary>
public class CheckpointService(
    ZileanDbContext dbContext,
    ILogger<CheckpointService> logger)
{
    /// <summary>
    /// Get a checkpoint value by source type and key.
    /// </summary>
    public async Task<string?> GetCheckpointAsync(SourceType sourceType, string key, CancellationToken cancellationToken)
    {
        var checkpoint = await dbContext.IngestionCheckpoints
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SourceType == sourceType && c.CheckpointKey == key, cancellationToken);
        
        return checkpoint?.CheckpointValue;
    }

    /// <summary>
    /// Set or update a checkpoint value.
    /// </summary>
    public async Task SetCheckpointAsync(SourceType sourceType, string key, string value, CancellationToken cancellationToken)
    {
        var checkpoint = await dbContext.IngestionCheckpoints
            .FirstOrDefaultAsync(c => c.SourceType == sourceType && c.CheckpointKey == key, cancellationToken);
        
        if (checkpoint is null)
        {
            checkpoint = new IngestionCheckpoint
            {
                SourceType = sourceType,
                CheckpointKey = key,
                CheckpointValue = value,
                UpdatedAt = DateTime.UtcNow
            };
            await dbContext.IngestionCheckpoints.AddAsync(checkpoint, cancellationToken);
        }
        else
        {
            checkpoint.CheckpointValue = value;
            checkpoint.UpdatedAt = DateTime.UtcNow;
        }
        
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Checkpoint set: {SourceType}:{Key} = {Value}", sourceType, key, value);
    }

    /// <summary>
    /// Delete a checkpoint.
    /// </summary>
    public async Task DeleteCheckpointAsync(SourceType sourceType, string key, CancellationToken cancellationToken)
    {
        var checkpoint = await dbContext.IngestionCheckpoints
            .FirstOrDefaultAsync(c => c.SourceType == sourceType && c.CheckpointKey == key, cancellationToken);
        
        if (checkpoint is not null)
        {
            dbContext.IngestionCheckpoints.Remove(checkpoint);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogDebug("Checkpoint deleted: {SourceType}:{Key}", sourceType, key);
        }
    }

    /// <summary>
    /// Get all checkpoints for a source type.
    /// </summary>
    public async Task<List<IngestionCheckpoint>> GetAllCheckpointsAsync(SourceType sourceType, CancellationToken cancellationToken)
    {
        return await dbContext.IngestionCheckpoints
            .AsNoTracking()
            .Where(c => c.SourceType == sourceType)
            .ToListAsync(cancellationToken);
    }
}
