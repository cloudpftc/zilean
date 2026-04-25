using Microsoft.AspNetCore.Mvc;
using Zilean.ApiService.Features.Audit;
using Zilean.Shared.Features.Audit;
using Zilean.Shared.Features.Configuration;

namespace Zilean.ApiService.Features.Audit;

public static class QueryAuditEndpoints
{
    private const string GroupName = "audit/queries";

    public static WebApplication MapQueryAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(GroupName)
            .WithTags(GroupName)
            .RequireAuthorization();

        group.MapGet("/recent", GetRecentQueries);
        group.MapGet("/top", GetTopQueries);
        group.MapGet("/range", GetQueriesByDateRange);

        return app;
    }

    private static async Task<IResult> GetRecentQueries(
        [FromServices] IQueryAuditService service,
        [FromServices] ZileanConfiguration configuration,
        [FromQuery] int limit = 100)
    {
        if (!configuration.Audit.EnableQueryAuditing)
        {
            return TypedResults.NotFound();
        }

        var data = await service.GetRecentQueriesAsync(limit);
        return TypedResults.Ok(data);
    }

    private static async Task<IResult> GetTopQueries(
        [FromServices] IQueryAuditService service,
        [FromServices] ZileanConfiguration configuration,
        [FromQuery] int limit = 20)
    {
        if (!configuration.Audit.EnableQueryAuditing)
        {
            return TypedResults.NotFound();
        }

        var data = await service.GetTopQueriesAsync(limit);
        return TypedResults.Ok(data);
    }

    private static async Task<IResult> GetQueriesByDateRange(
        [FromServices] IQueryAuditService service,
        [FromServices] ZileanConfiguration configuration,
        [AsParameters] DateRangeQuery request)
    {
        if (!configuration.Audit.EnableQueryAuditing)
        {
            return TypedResults.NotFound();
        }

        var data = await service.GetQueriesByDateRangeAsync(request.Start, request.End);
        return TypedResults.Ok(data);
    }

    public record DateRangeQuery(DateTime Start, DateTime End);
}
