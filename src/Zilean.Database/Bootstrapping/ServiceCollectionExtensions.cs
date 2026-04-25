namespace Zilean.Database.Bootstrapping;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddZileanDataServices(this IServiceCollection services, ZileanConfiguration configuration)
    {
        services.AddDbContext<ZileanDbContext>(options => options.UseNpgsql(configuration.Database.ConnectionString));
        services.AddTransient<ITorrentInfoService, TorrentInfoService>();
        services.AddTransient<IImdbFileService, ImdbFileService>();
        services.RegisterImdbMatchingService(configuration);

        return services;
    }

    private static void RegisterImdbMatchingService(this IServiceCollection services, ZileanConfiguration configuration)
    {
        services.AddTransient<IImdbMatchingService, ImdbFuzzyStringMatchingService>();
    }
}
