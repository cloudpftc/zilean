using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Zilean.Shared.Features.Configuration;

namespace Zilean.Database.Bootstrapping;

public class SynchronousCommitInterceptor : DbConnectionInterceptor
{
    private readonly string _mode;

    public SynchronousCommitInterceptor(PersistenceSettings settings)
    {
        _mode = settings.SynchronousCommitMode;
    }

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ExecuteSetCommand(connection);
        return base.ConnectionOpening(connection, eventData, result);
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        ExecuteSetCommand(connection);
        return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }

    private void ExecuteSetCommand(DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET synchronous_commit = '{_mode}';";
        cmd.ExecuteNonQuery();
    }
}
