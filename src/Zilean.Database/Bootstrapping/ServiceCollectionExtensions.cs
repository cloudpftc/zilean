using Npgsql;
using Zilean.Database.Services.Postgres;
using Zilean.Shared.Features.Configuration;

namespace Zilean.Database.Bootstrapping;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddZileanDataServices(this IServiceCollection services, ZileanConfiguration configuration)
    {
        var persistence = configuration.Persistence;
        var optimizedConnectionString = BuildOptimizedConnectionString(configuration.Database.ConnectionString, persistence);

        var interceptor = new SynchronousCommitInterceptor(persistence);

        services.AddDbContext<ZileanDbContext>(options =>
            options
                .UseNpgsql(optimizedConnectionString, npgsql => npgsql.CommandTimeout(persistence.CommandTimeoutSeconds))
                .AddInterceptors(interceptor));

        services.AddTransient<ITorrentInfoService, TorrentInfoService>();
        services.AddTransient<IImdbFileService, ImdbFileService>();
        services.RegisterImdbMatchingService(configuration);

        return services;
    }

    private static string BuildOptimizedConnectionString(string baseConnectionString, PersistenceSettings settings)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString);
        builder.MaxPoolSize = settings.MaxPoolSize;
        builder.MinPoolSize = settings.MinPoolSize;
        builder.Timeout = settings.ConnectionTimeout;
        return builder.ToString();
    }

    private static void RegisterImdbMatchingService(this IServiceCollection services, ZileanConfiguration configuration)
    {
        services.AddTransient<IImdbMatchingService, ImdbPostgresMatchingService>();
    }
}
