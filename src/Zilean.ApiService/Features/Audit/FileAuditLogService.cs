using Microsoft.Extensions.Logging;
using Zilean.Shared.Features.Audit;

namespace Zilean.ApiService.Features.Audit;

public class FileAuditLogService : IFileAuditLogService
{
    private readonly ZileanDbContext _dbContext;
    private readonly ILogger<FileAuditLogService> _logger;

    public FileAuditLogService(ZileanDbContext dbContext, ILogger<FileAuditLogService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogFileOperationAsync(string operation, string? filePath, string status, string? details, int durationMs)
    {
        var audit = new FileAuditLog
        {
            Operation = operation,
            FilePath = filePath,
            Status = status,
            Details = details,
            DurationMs = durationMs,
            Timestamp = DateTime.UtcNow,
        };

        _dbContext.FileAuditLogs.Add(audit);
        await _dbContext.SaveChangesAsync();

        _logger.LogDebug("Logged file audit: {Operation} on {FilePath} -> {Status} in {DurationMs}ms", operation, filePath, status, durationMs);
    }

    public async Task<IEnumerable<FileAuditLog>> GetRecentLogsAsync(int limit = 100)
    {
        return await _dbContext.FileAuditLogs
            .OrderByDescending(f => f.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<FileAuditLog>> GetLogsByOperationAsync(string operation, int limit = 50)
    {
        return await _dbContext.FileAuditLogs
            .Where(f => f.Operation == operation)
            .OrderByDescending(f => f.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<FileAuditLog>> GetLogsByDateRangeAsync(DateTime start, DateTime end)
    {
        return await _dbContext.FileAuditLogs
            .Where(f => f.Timestamp >= start && f.Timestamp <= end)
            .OrderByDescending(f => f.Timestamp)
            .ToListAsync();
    }
}
