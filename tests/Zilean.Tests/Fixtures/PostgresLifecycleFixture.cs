using Npgsql;

namespace Zilean.Tests.Fixtures;

public class PostgresLifecycleFixture : IAsyncLifetime
{
    private const string ServerConnectionString = "Host=localhost;Port=15432;Database=postgres;Username=postgres;Password=postgres";
    private readonly string _testDbName = $"zilean_test_{Guid.NewGuid():N}";

    public ZileanConfiguration ZileanConfiguration { get; }

    public PostgresLifecycleFixture()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")))
        {
            Environment.SetEnvironmentVariable("POSTGRES_PASSWORD", "postgres");
        }

        ZileanConfiguration = new ZileanConfiguration();

        DerivePathInfo(
            (_, projectDirectory, type, method) => new(
                directory: Path.Combine(projectDirectory, "Verification"),
                typeName: type.Name,
                methodName: method.Name));
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ServerConnectionString);
        await conn.OpenAsync();

        await using var createDb = conn.CreateCommand();
        createDb.CommandText = $"CREATE DATABASE \"{_testDbName}\"";
        await createDb.ExecuteNonQueryAsync();

        ZileanConfiguration.Database.ConnectionString =
            $"Host=localhost;Port=15432;Database={_testDbName};Username=postgres;Password=postgres";
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(ServerConnectionString);
        await conn.OpenAsync();

        await using var killConnections = conn.CreateCommand();
        killConnections.CommandText = $"""
            SELECT pg_terminate_backend(pg_stat_activity.pid)
            FROM pg_stat_activity
            WHERE pg_stat_activity.datname = '{_testDbName}'
              AND pid <> pg_backend_pid();
            """;
        await killConnections.ExecuteNonQueryAsync();

        await using var dropDb = conn.CreateCommand();
        dropDb.CommandText = $"DROP DATABASE IF EXISTS \"{_testDbName}\"";
        await dropDb.ExecuteNonQueryAsync();
    }
}
