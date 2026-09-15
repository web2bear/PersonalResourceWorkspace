using Microsoft.Data.Sqlite;

namespace PersonalResourceWorkspace.Infrastructure.Persistence;

public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken);
}
