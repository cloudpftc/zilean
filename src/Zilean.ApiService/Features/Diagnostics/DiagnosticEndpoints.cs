namespace Zilean.ApiService.Features.Diagnostics;

public static class DiagnosticEndpoints
{
    private const string GroupName = "diagnostics";

    public static WebApplication MapDiagnosticEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(GroupName)
            .WithTags(GroupName)
            .RequireAuthorization();

        group.MapGet("/freshness", GetFreshness);
        group.MapGet("/queue", GetQueue);
        group.MapGet("/misses", GetMisses);
        group.MapGet("/stats", GetStats);

        return app;
    }

    private static IResult GetFreshness() => Results.Ok(new
    {
        status = "not_implemented",
        message = "Freshness tracking coming soon"
    });

    private static IResult GetQueue() => Results.Ok(new
    {
        status = "not_implemented",
        message = "Queue tracking coming soon"
    });

    private static IResult GetMisses() => Results.Ok(new
    {
        status = "not_implemented",
        message = "Miss tracking coming soon"
    });

    private static IResult GetStats() => Results.Ok(new
    {
        status = "not_implemented",
        message = "Stats endpoint coming soon"
    });
}
