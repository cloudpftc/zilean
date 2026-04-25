using Zilean.Shared.Features.Audit;

namespace Zilean.ApiService.Features.Audit;

/// <summary>
/// Provides file audit logging and retrieval operations.
/// </summary>
public interface IFileAuditLogService
{
    /// <summary>
    /// Logs a file operation to the audit table.
    /// </summary>
    /// <param name="operation">The type of file operation performed.</param>
    /// <param name="filePath">The file path involved in the operation, if applicable.</param>
    /// <param name="status">The outcome status of the operation.</param>
    /// <param name="details">Additional details about the operation.</param>
    /// <param name="durationMs">Operation execution time in milliseconds.</param>
    Task LogFileOperationAsync(string operation, string? filePath, string status, string? details, int durationMs);

    /// <summary>
    /// Returns the most recent file audit logs.
    /// </summary>
    /// <param name="limit">Maximum number of records to return (default 100).</param>
    Task<IEnumerable<FileAuditLog>> GetRecentLogsAsync(int limit = 100);

    /// <summary>
    /// Returns file audit logs filtered by operation type.
    /// </summary>
    /// <param name="operation">The operation type to filter by.</param>
    /// <param name="limit">Maximum number of records to return (default 50).</param>
    Task<IEnumerable<FileAuditLog>> GetLogsByOperationAsync(string operation, int limit = 50);

    /// <summary>
    /// Returns file audit logs within a date range.
    /// </summary>
    Task<IEnumerable<FileAuditLog>> GetLogsByDateRangeAsync(DateTime start, DateTime end);
}
