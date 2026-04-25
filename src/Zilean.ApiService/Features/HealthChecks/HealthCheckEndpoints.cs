namespace Zilean.ApiService.Features.HealthChecks;

public static class HealthCheckEndpoints
{
    private const string GroupName = "healthchecks";
    private const string Ping = "/ping";
    private const string Health = "/health";

    public static WebApplication MapHealthCheckEndpoints(this WebApplication app)
    {
        app.MapGroup(GroupName)
            .WithTags(GroupName)
            .HealthChecks()
            .DisableAntiforgery()
            .AllowAnonymous();

        return app;
    }

    private static RouteGroupBuilder HealthChecks(this RouteGroupBuilder group)
    {
        group.MapGet(Ping, RespondPong);
        group.MapGet(Health, GetHealth);

        return group;
    }

    private static string RespondPong(HttpContext context) => $"[{DateTime.UtcNow.ToString(CultureInfo.InvariantCulture)}]: Pong!";

    private static async Task<IResult> GetHealth(IHealthCheckService healthCheckService)
    {
        var overallHealth = await healthCheckService.GetOverallHealthAsync();

        if (overallHealth.IsHealthy)
        {
            return TypedResults.Ok(overallHealth);
        }

        return Results.Json(overallHealth, statusCode: 503);
    }
}
