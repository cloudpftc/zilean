namespace Zilean.Shared.Features.Configuration;

public class DatabaseConfiguration
{
  public string ConnectionString { get; set; }

  public DatabaseConfiguration()
  {
    var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "postgres";
    var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
    if (string.IsNullOrWhiteSpace(password))
    {
      throw new InvalidOperationException("Environment variable POSTGRES_PASSWORD is not set.");
    }

    ConnectionString = $"Host={host};Database=zilean;Username=postgres;Password={password};Include Error Detail=true;Timeout=30;CommandTimeout=3600;";
  }
}
