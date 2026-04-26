using Serilog;

var logsDir = Path.Combine(AppContext.BaseDirectory, "data", "logs");
Directory.CreateDirectory(logsDir);

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddConfigurationFiles();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

builder.AddOtlpServiceDefaults();

var zileanConfiguration = builder.Configuration.GetZileanConfiguration();

builder.Services
    .AddConfiguration(zileanConfiguration)
    .AddSwaggerSupport()
    .AddSchedulingSupport()
    .AddShellExecutionService()
    .ConditionallyRegisterDmmJob(zileanConfiguration)
    .AddZileanDataServices(zileanConfiguration)
    .AddApiKeyAuthentication()
    .AddHealthCheckService()
    .AddQueryAuditService()
    .AddQueryCacheService()
    .AddMissTrackingService()
    .AddIngestionQueueService()
    .AddIngestionCheckpointService()
    .AddFileAuditService()
    .AddStartupHostedServices()
    .AddDashboardSupport(zileanConfiguration);

var app = builder.Build();

app.UseZileanRequired(zileanConfiguration);
app.MapZileanEndpoints(zileanConfiguration);
app.Services.SetupScheduling(zileanConfiguration);

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
