using Microsoft.Data.Sqlite;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Resources;

namespace PersonalResourceWorkspace.Infrastructure.Persistence;

internal sealed class SqliteWorkspaceInitializer(
    ISqliteConnectionFactory connectionFactory,
    IAppDataPathProvider pathProvider) : IApplicationInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = Path.Combine(pathProvider.GetLocalDataRoot(), "Data");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "workspace.db");
        if (File.Exists(databasePath) && !await IsCurrentSchemaAsync(databasePath, cancellationToken).ConfigureAwait(false))
        {
            await BackupAndRetireLegacyDatabaseAsync(databasePath, cancellationToken).ConfigureAwait(false);
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS Resources (
                    Id TEXT NOT NULL PRIMARY KEY, Title TEXT NOT NULL, Description TEXT NOT NULL,
                    Kind TEXT NOT NULL, Modified TEXT NOT NULL, Glyph TEXT NOT NULL,
                    Address TEXT NOT NULL, VisualMode TEXT NOT NULL, VisualKind TEXT NOT NULL,
                    VisualAssetPath TEXT NULL, VisualOrigin TEXT NOT NULL,
                    VisualSourceFingerprint TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Tags (
                    Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL,
                    NormalizedName TEXT NOT NULL UNIQUE, IsSystem INTEGER NOT NULL CHECK(IsSystem IN (0,1))
                );
                CREATE TABLE IF NOT EXISTS ResourceTags (
                    ResourceId TEXT NOT NULL REFERENCES Resources(Id) ON DELETE CASCADE,
                    TagId TEXT NOT NULL REFERENCES Tags(Id) ON DELETE RESTRICT,
                    PRIMARY KEY(ResourceId, TagId)
                );
                CREATE TABLE IF NOT EXISTS SavedSearches (
                    Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL,
                    ParentId TEXT NULL REFERENCES SavedSearches(Id) ON DELETE CASCADE,
                    Glyph TEXT NOT NULL, Color TEXT NOT NULL, SortOrder INTEGER NOT NULL,
                    IsSystem INTEGER NOT NULL CHECK(IsSystem IN (0,1)),
                    CHECK(ParentId IS NULL OR ParentId <> Id)
                );
                CREATE TABLE IF NOT EXISTS SavedSearchTags (
                    SearchId TEXT NOT NULL REFERENCES SavedSearches(Id) ON DELETE CASCADE,
                    TagId TEXT NOT NULL REFERENCES Tags(Id) ON DELETE RESTRICT,
                    PRIMARY KEY(SearchId, TagId)
                );
                CREATE TABLE IF NOT EXISTS Settings (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS WorkspaceSchema (Version INTEGER NOT NULL PRIMARY KEY);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            command.CommandText =
                """
                INSERT OR IGNORE INTO Tags VALUES ($inbox, 'Входящие', 'входящие', 1);
                INSERT OR IGNORE INTO Tags VALUES ($trash, 'Корзина', 'корзина', 1);
                INSERT OR IGNORE INTO SavedSearches VALUES ($inboxSearch, 'Входящие', NULL, '', '#7EA7FF', 0, 1);
                INSERT OR IGNORE INTO SavedSearches VALUES ($trashSearch, 'Корзина', NULL, '', '#E25A5A', 1, 1);
                INSERT OR IGNORE INTO SavedSearchTags VALUES ($inboxSearch, $inbox);
                INSERT OR IGNORE INTO SavedSearchTags VALUES ($trashSearch, $trash);
                INSERT OR IGNORE INTO WorkspaceSchema VALUES (1);
                """;
            command.Parameters.AddWithValue("$inbox", SystemTags.InboxId.ToString());
            command.Parameters.AddWithValue("$trash", SystemTags.TrashId.ToString());
            command.Parameters.AddWithValue("$inboxSearch", SystemTags.InboxSearchId.ToString());
            command.Parameters.AddWithValue("$trashSearch", SystemTags.TrashSearchId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<bool> IsCurrentSchemaAsync(string databasePath, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='WorkspaceSchema');";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0) return false;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM WorkspaceSchema WHERE Version=1);";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static async Task BackupAndRetireLegacyDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(Path.GetDirectoryName(databasePath)!, $"workspace.collections-backup-{stamp}-{Guid.NewGuid():N}.db");
        var sourceBuilder = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly };
        var backupBuilder = new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadWriteCreate };
        try
        {
            await using (var source = new SqliteConnection(sourceBuilder.ToString()))
            await using (var backup = new SqliteConnection(backupBuilder.ToString()))
            {
                await source.OpenAsync(cancellationToken).ConfigureAwait(false);
                await backup.OpenAsync(cancellationToken).ConfigureAwait(false);
                source.BackupDatabase(backup);
                await using var check = backup.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals((string?)await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Не удалось проверить резервную копию SQLite.");
            }

            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
        }
        catch
        {
            // A failed backup must never cause the original database to be retired.
            throw;
        }
    }
}
