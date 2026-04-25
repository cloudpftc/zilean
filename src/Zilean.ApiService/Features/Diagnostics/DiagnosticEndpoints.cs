namespace Zilean.ApiService.Features.Diagnostics;

public static class DiagnosticEndpoints
{
    private const string GroupName = "diagnostics";
    private const string Health = "/health";

    public static WebApplication MapDiagnosticEndpoints(this WebApplication app)
    {
        app.MapGroup(GroupName)
            .WithTags(GroupName)
            .MapDiagnosticHealth()
            .DisableAntiforgery()
            .AllowAnonymous();

        return app;
    }

    private static RouteGroupBuilder MapDiagnosticHealth(this RouteGroupBuilder group)
    {
        group.MapGet(Health, async (IHealthCheckService healthCheckService, CancellationToken ct) =>
        {
            var status = await healthCheckService.GetHealthStatusAsync(ct);
            return Results.Ok(status);
        });

        return group;
    }
}
