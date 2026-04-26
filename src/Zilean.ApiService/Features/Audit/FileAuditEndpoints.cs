using Zilean.ApiService.Features.Audit;
using Zilean.Shared.Features.Audit;

namespace Zilean.ApiService.Features.Audit;

public static class FileAuditEndpoints
{
    private const string GroupName = "api/audit/files";

    public static WebApplication MapFileAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(GroupName)
            .WithTags(GroupName);

        group.MapGet("/recent", GetRecentLogs);
        group.MapGet("/by-operation", GetLogsByOperation);
        group.MapGet("/range", GetLogsByDateRange);

        return app;
    }

    private static async Task<IResult> GetRecentLogs(
        [AsParameters] RecentLogsQuery query,
        IFileAuditLogService auditService)
    {
        var logs = await auditService.GetRecentLogsAsync(query.Limit);
        return TypedResults.Ok(logs);
    }

    private static async Task<IResult> GetLogsByOperation(
        [AsParameters] ByOperationQuery query,
        IFileAuditLogService auditService)
    {
        var logs = await auditService.GetLogsByOperationAsync(query.Operation, query.Limit);
        return TypedResults.Ok(logs);
    }

    private static async Task<IResult> GetLogsByDateRange(
        [AsParameters] ByDateRangeQuery query,
        IFileAuditLogService auditService)
    {
        var logs = await auditService.GetLogsByDateRangeAsync(query.Start, query.End);
        return TypedResults.Ok(logs);
    }

    public record RecentLogsQuery(int Limit = 100);

    public record ByOperationQuery(string Operation, int Limit = 50);

    public record ByDateRangeQuery(DateTime Start, DateTime End);
}
