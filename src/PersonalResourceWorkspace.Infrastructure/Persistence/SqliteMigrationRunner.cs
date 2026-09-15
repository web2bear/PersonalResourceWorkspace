using Microsoft.Data.Sqlite;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Collections;
using System.Globalization;

namespace PersonalResourceWorkspace.Infrastructure.Persistence;

internal sealed class SqliteMigrationRunner(ISqliteConnectionFactory connectionFactory) : IApplicationInitializer
{
    private const int FoundationMigrationVersion = 1;
    private const int CollectionsMigrationVersion = 2;
    private const int TrashCollectionMigrationVersion = 3;
    private const int ResourceVisualsMigrationVersion = 4;
    private const int CollectionVisualsMigrationVersion = 5;

    private static readonly (string Name, string Glyph, string Color, int SortOrder, bool IsDefault)[] DefaultCollections =
    [
        ("Работа", "\uE8B7", "#FF9B72", 0, false),
        ("Дизайн", "\uE790", "#58C7B6", 1, false),
        ("Идеи", "\uEA80", "#F4C95D", 2, false),
        ("Читать позже", "\uE8C3", "#A995FF", 3, false),
        ("Входящие", "\uE896", "#7EA7FF", 4, true),
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureMigrationHistoryAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await CreateResourcesTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (!await IsAppliedAsync(connection, transaction, FoundationMigrationVersion, cancellationToken).ConfigureAwait(false))
            {
                await RecordMigrationAsync(connection, transaction, FoundationMigrationVersion, cancellationToken).ConfigureAwait(false);
            }

            if (!await IsAppliedAsync(connection, transaction, CollectionsMigrationVersion, cancellationToken).ConfigureAwait(false))
            {
                await CreateCollectionsTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                await SeedCollectionsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                await RecordMigrationAsync(connection, transaction, CollectionsMigrationVersion, cancellationToken).ConfigureAwait(false);
            }

            if (!await IsAppliedAsync(connection, transaction, TrashCollectionMigrationVersion, cancellationToken).ConfigureAwait(false))
            {
                await EnsureTrashColumnAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                await SeedTrashCollectionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                await RecordMigrationAsync(connection, transaction, TrashCollectionMigrationVersion, cancellationToken).ConfigureAwait(false);
            }

            if (!await IsAppliedAsync(connection, transaction, ResourceVisualsMigrationVersion, cancellationToken).ConfigureAwait(false))
            {
                await EnsureResourceVisualColumnsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                await RecordMigrationAsync(connection, transaction, ResourceVisualsMigrationVersion, cancellationToken).ConfigureAwait(false);
            }

            if (!await IsAppliedAsync(connection, transaction, CollectionVisualsMigrationVersion, cancellationToken).ConfigureAwait(false))
            {
                if (!await ColumnExistsAsync(connection, transaction, "Collections", "VisualAssetPath", cancellationToken).ConfigureAwait(false))
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "ALTER TABLE Collections ADD COLUMN VisualAssetPath TEXT NULL;";
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await RecordMigrationAsync(connection, transaction, CollectionVisualsMigrationVersion, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CreateResourcesTableAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS Resources (Id TEXT NOT NULL PRIMARY KEY, Title TEXT NOT NULL, Description TEXT NOT NULL, Kind TEXT NOT NULL, Collection TEXT NOT NULL, Modified TEXT NOT NULL, Glyph TEXT NOT NULL, IsFavorite INTEGER NOT NULL CHECK (IsFavorite IN (0, 1)), Address TEXT NOT NULL, VisualMode TEXT NOT NULL DEFAULT 'Auto', VisualKind TEXT NOT NULL DEFAULT 'TypeFallback', VisualAssetPath TEXT NULL, VisualOrigin TEXT NOT NULL DEFAULT 'Fallback', VisualSourceFingerprint TEXT NOT NULL DEFAULT '');";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureResourceVisualColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var columns = new (string Name, string Definition)[]
        {
            ("VisualMode", "TEXT NOT NULL DEFAULT 'Auto'"),
            ("VisualKind", "TEXT NOT NULL DEFAULT 'TypeFallback'"),
            ("VisualAssetPath", "TEXT NULL"),
            ("VisualOrigin", "TEXT NOT NULL DEFAULT 'Fallback'"),
            ("VisualSourceFingerprint", "TEXT NOT NULL DEFAULT ''"),
        };

        foreach (var column in columns)
        {
            if (await ColumnExistsAsync(connection, transaction, "Resources", column.Name, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE Resources ADD COLUMN {column.Name} {column.Definition};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CreateCollectionsTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Collections (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                Glyph TEXT NOT NULL,
                Color TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                IsDefault INTEGER NOT NULL CHECK (IsDefault IN (0, 1)),
                IsTrash INTEGER NOT NULL DEFAULT 0 CHECK (IsTrash IN (0, 1)),
                VisualAssetPath TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedCollectionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        foreach (var collection in DefaultCollections)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO Collections (Id, Name, Glyph, Color, SortOrder, IsDefault, IsTrash)
                VALUES ($id, $name, $glyph, $color, $sortOrder, $isDefault, 0)
                ON CONFLICT(Name) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$name", collection.Name);
            command.Parameters.AddWithValue("$glyph", collection.Glyph);
            command.Parameters.AddWithValue("$color", collection.Color);
            command.Parameters.AddWithValue("$sortOrder", collection.SortOrder);
            command.Parameters.AddWithValue("$isDefault", collection.IsDefault ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var distinctCommand = connection.CreateCommand();
        distinctCommand.Transaction = transaction;
        distinctCommand.CommandText =
            "SELECT DISTINCT Collection FROM Resources WHERE Collection NOT IN (SELECT Name FROM Collections);";
        await using var reader = await distinctCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var extraNames = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            extraNames.Add(reader.GetString(0));
        }

        var sortOrder = DefaultCollections.Length;
        foreach (var name in extraNames)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO Collections (Id, Name, Glyph, Color, SortOrder, IsDefault, IsTrash)
                VALUES ($id, $name, $glyph, $color, $sortOrder, 0, 0);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$glyph", "\uE8B7");
            command.Parameters.AddWithValue("$color", "#7EA7FF");
            command.Parameters.AddWithValue("$sortOrder", sortOrder++);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureTrashColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(connection, transaction, "Collections", "IsTrash", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "ALTER TABLE Collections ADD COLUMN IsTrash INTEGER NOT NULL DEFAULT 0 CHECK (IsTrash IN (0, 1));";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedTrashCollectionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var maxOrderCommand = connection.CreateCommand();
        maxOrderCommand.Transaction = transaction;
        maxOrderCommand.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) FROM Collections;";
        var maxOrder = Convert.ToInt32(
            await maxOrderCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Collections (Id, Name, Glyph, Color, SortOrder, IsDefault, IsTrash)
            VALUES ($id, $name, $glyph, $color, $sortOrder, 0, 1)
            ON CONFLICT(Name) DO UPDATE SET IsTrash = 1;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$name", SystemCollections.TrashName);
        command.Parameters.AddWithValue("$glyph", SystemCollections.TrashGlyph);
        command.Parameters.AddWithValue("$color", SystemCollections.TrashColor);
        command.Parameters.AddWithValue("$sortOrder", maxOrder + 1);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task EnsureMigrationHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Version INTEGER NOT NULL PRIMARY KEY,
                AppliedAtUtc TEXT NOT NULL
                    CHECK (AppliedAtUtc GLOB '????-??-??T??:??:??*Z')
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsAppliedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM SchemaMigrations WHERE Version = $version);";
        command.Parameters.AddWithValue("$version", version);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task RecordMigrationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO SchemaMigrations (Version, AppliedAtUtc) VALUES ($version, $appliedAtUtc);";
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$appliedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
