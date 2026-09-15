using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Infrastructure.Persistence;
using System.Globalization;

namespace PersonalResourceWorkspace.Infrastructure.Tests.Persistence;

public sealed class SqliteMigrationTests : IAsyncLifetime
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), $"prw-tests-{Guid.NewGuid():N}");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task InitializeAsyncCreatesVersionedHistoryAndEnablesForeignKeys()
    {
        await using var provider = CreateServices();
        var initializer = provider.GetRequiredService<IApplicationInitializer>();

        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        var connectionFactory = provider.GetRequiredService<ISqliteConnectionFactory>();
        await using SqliteConnection connection = await connectionFactory.OpenAsync(CancellationToken.None);
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 1;";
        Assert.Equal(
            1L,
            Convert.ToInt64(
                await versionCommand.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));

        await using var collectionsVersionCommand = connection.CreateCommand();
        collectionsVersionCommand.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 2;";
        Assert.Equal(
            1L,
            Convert.ToInt64(
                await collectionsVersionCommand.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));

        await using var collectionsCommand = connection.CreateCommand();
        collectionsCommand.CommandText = "SELECT COUNT(*) FROM Collections;";
        Assert.Equal(
            6L,
            Convert.ToInt64(
                await collectionsCommand.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));

        await using var trashVersionCommand = connection.CreateCommand();
        trashVersionCommand.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 3;";
        Assert.Equal(
            1L,
            Convert.ToInt64(
                await trashVersionCommand.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));

        await using var trashCommand = connection.CreateCommand();
        trashCommand.CommandText = "SELECT COUNT(*) FROM Collections WHERE IsTrash = 1 AND Name = 'Корзина';";
        Assert.Equal(
            1L,
            Convert.ToInt64(
                await trashCommand.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));

        await using var visualsVersionCommand = connection.CreateCommand();
        visualsVersionCommand.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version IN (4, 5);";
        Assert.Equal(
            2L,
            Convert.ToInt64(
                await visualsVersionCommand.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));

        await using var resourceVisualColumns = connection.CreateCommand();
        resourceVisualColumns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Resources') WHERE name LIKE 'Visual%';";
        Assert.Equal(
            5L,
            Convert.ToInt64(
                await resourceVisualColumns.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));

        await using var collectionVisualColumns = connection.CreateCommand();
        collectionVisualColumns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Collections') WHERE name = 'VisualAssetPath';";
        Assert.Equal(
            1L,
            Convert.ToInt64(
                await collectionVisualColumns.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));

        await using var foreignKeysCommand = connection.CreateCommand();
        foreignKeysCommand.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(
            1L,
            Convert.ToInt64(
                await foreignKeysCommand.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task InitializeAsyncAddsTrashToExistingCollectionsSchema()
    {
        await using var provider = CreateServices();
        var connectionFactory = provider.GetRequiredService<ISqliteConnectionFactory>();
        await using (var connection = await connectionFactory.OpenAsync(CancellationToken.None))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE SchemaMigrations (
                    Version INTEGER NOT NULL PRIMARY KEY,
                    AppliedAtUtc TEXT NOT NULL
                );
                CREATE TABLE Resources (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    Kind TEXT NOT NULL,
                    Collection TEXT NOT NULL,
                    Modified TEXT NOT NULL,
                    Glyph TEXT NOT NULL,
                    IsFavorite INTEGER NOT NULL,
                    Address TEXT NOT NULL
                );
                CREATE TABLE Collections (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    Glyph TEXT NOT NULL,
                    Color TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    IsDefault INTEGER NOT NULL
                );
                INSERT INTO SchemaMigrations (Version, AppliedAtUtc) VALUES (1, '2026-01-01T00:00:00.0000000Z');
                INSERT INTO SchemaMigrations (Version, AppliedAtUtc) VALUES (2, '2026-01-01T00:00:00.0000000Z');
                INSERT INTO Collections (Id, Name, Glyph, Color, SortOrder, IsDefault)
                VALUES ('11111111-1111-1111-1111-111111111111', 'Входящие', 'E', '#7EA7FF', 0, 1);
                INSERT INTO Collections (Id, Name, Glyph, Color, SortOrder, IsDefault)
                VALUES ('22222222-2222-2222-2222-222222222222', 'Корзина', 'X', '#FF0000', 1, 0);
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await provider.GetRequiredService<IApplicationInitializer>().InitializeAsync(CancellationToken.None);

        await using var upgraded = await connectionFactory.OpenAsync(CancellationToken.None);
        await using var trashCommand = upgraded.CreateCommand();
        trashCommand.CommandText = "SELECT COUNT(*) FROM Collections WHERE IsTrash = 1 AND Name = 'Корзина';";
        Assert.Equal(
            1L,
            Convert.ToInt64(
                await trashCommand.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));

        await using var inboxCommand = upgraded.CreateCommand();
        inboxCommand.CommandText = "SELECT IsTrash FROM Collections WHERE Name = 'Входящие';";
        Assert.Equal(
            0L,
            Convert.ToInt64(
                await inboxCommand.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));

        await using var versionCommand = upgraded.CreateCommand();
        versionCommand.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 3;";
        Assert.Equal(
            1L,
            Convert.ToInt64(
                await versionCommand.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture));
    }

    private ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppDataPathProvider>(new TestAppDataPathProvider(_temporaryRoot));
        services.AddInfrastructure();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class TestAppDataPathProvider(string path) : IAppDataPathProvider
    {
        public string GetLocalDataRoot() => path;
    }
}
