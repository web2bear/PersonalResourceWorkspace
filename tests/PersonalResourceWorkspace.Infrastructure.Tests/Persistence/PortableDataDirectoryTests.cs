using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalResourceWorkspace.Infrastructure.Storage;

namespace PersonalResourceWorkspace.Infrastructure.Tests.Persistence;

public sealed class PortableDataDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prw-portable-{Guid.NewGuid():N}");

    [Fact]
    public void ResolvesSourcesInPriorityOrderRelativeToExecutable()
    {
        Directory.CreateDirectory(_root);
        Assert.Equal(Path.Combine(_root, "content"), new PortableDataDirectory(_root, [], null).GetLocalDataRoot());
        File.WriteAllText(Path.Combine(_root, "portable-config.json"), "{\"dataDirectory\":\"json-data\"}");
        Assert.Equal(Path.Combine(_root, "json-data"), new PortableDataDirectory(_root, [], null).GetLocalDataRoot());
        Assert.Equal(Path.Combine(_root, "env-data"), new PortableDataDirectory(_root, [], "env-data").GetLocalDataRoot());
        var cli = new PortableDataDirectory(_root, ["--data-dir", "cli-data"], "env-data");
        Assert.Equal(Path.Combine(_root, "cli-data"), cli.GetLocalDataRoot());
        Assert.True(cli.IsOverridden);
    }

    [Fact]
    public async Task RelocateCopiesDatabaseAndAssetsThenUpdatesConfig()
    {
        var source = Path.Combine(_root, "content");
        Directory.CreateDirectory(Path.Combine(source, "Data"));
        Directory.CreateDirectory(Path.Combine(source, "Data", "Visuals", "user"));
        File.WriteAllText(Path.Combine(source, "Data", "Visuals", "user", "icon.txt"), "asset");
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(source, "Data", "workspace.db")}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Sample (Value TEXT); INSERT INTO Sample VALUES ('preserved');";
            command.ExecuteNonQuery();
        }

        var manager = new PortableDataDirectory(_root, [], null);
        var target = Path.Combine(_root, "moved");
        await manager.RelocateAsync(target, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(source, "Data", "workspace.db")));
        Assert.Equal("asset", File.ReadAllText(Path.Combine(target, "Data", "Visuals", "user", "icon.txt")));
        using var copied = new SqliteConnection($"Data Source={Path.Combine(target, "Data", "workspace.db")}");
        copied.Open();
        using var query = copied.CreateCommand();
        query.CommandText = "SELECT Value FROM Sample;";
        Assert.Equal("preserved", query.ExecuteScalar());
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "portable-config.json")));
        Assert.Equal(target, config.RootElement.GetProperty("dataDirectory").GetString());
    }

    [Fact]
    public async Task ExistingTargetIsNotOverwritten()
    {
        Directory.CreateDirectory(Path.Combine(_root, "content"));
        var target = Path.Combine(_root, "existing");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        var manager = new PortableDataDirectory(_root, [], null);
        await Assert.ThrowsAsync<IOException>(() => manager.RelocateAsync(target, CancellationToken.None));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "portable-config.json")));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
