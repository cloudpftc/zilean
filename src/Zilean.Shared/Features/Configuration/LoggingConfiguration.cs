using Microsoft.Extensions.Configuration;

namespace Zilean.Shared.Features.Configuration;

public static class LoggingConfiguration
{
    private const string DefaultLoggingContents =
        """
        {
          "Serilog": {
            "MinimumLevel": {
              "Default": "Debug",
              "Override": {
                "Microsoft": "Information",
                "System": "Warning",
                "System.Net.Http.HttpClient.Scraper.LogicalHandler": "Information",
                "System.Net.Http.HttpClient.Scraper.ClientHandler": "Information",
                "Microsoft.AspNetCore.Hosting.Diagnostics": "Error",
                "Microsoft.AspNetCore.DataProtection": "Error",
                "Microsoft.EntityFrameworkCore.Database.Command": "Information",
                "Coravel": "Information"
              }
            },
            "WriteTo": [
              {
                "Name": "File",
                "Args": {
                  "path": "/app/data/logs/zilean-.log",
                  "rollingInterval": "Day",
                  "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                  "rollOnFileSizeLimit": true,
                  "fileSizeLimitBytes": 104857600,
                  "retainedFileCountLimit": 14
                }
              }
            ]
          }
        }
        """;

    public static IConfigurationBuilder AddLoggingConfiguration(this IConfigurationBuilder configuration, string configurationFolderPath)
    {
        EnsureExists(configurationFolderPath);

        configuration.AddJsonFile(ConfigurationLiterals.LoggingConfigFilename, false, false);

        return configuration;
    }

    private static void EnsureExists(string configurationFolderPath)
    {
        var loggingPath = Path.Combine(configurationFolderPath, ConfigurationLiterals.LoggingConfigFilename);
        File.WriteAllText(loggingPath, DefaultLoggingContents);
    }
}
