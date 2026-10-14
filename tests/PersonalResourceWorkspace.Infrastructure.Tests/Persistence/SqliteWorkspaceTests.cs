using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Models;
using PersonalResourceWorkspace.Application.Resources;
using PersonalResourceWorkspace.Infrastructure;

namespace PersonalResourceWorkspace.Infrastructure.Tests.Persistence;

public sealed class SqliteWorkspaceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prw-tags-{Guid.NewGuid():N}");
    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        _provider = CreateServices();
        await _provider.GetRequiredService<IApplicationInitializer>().InitializeAsync(CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        _provider?.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    private IWorkspaceRepository Repository => _provider!.GetRequiredService<IWorkspaceRepository>();

    [Fact]
    public async Task NewDatabaseHasOnlySystemTagsAndSearches()
    {
        var tags = await Repository.GetTagsAsync(CancellationToken.None);
        var searches = await Repository.GetSearchesAsync(CancellationToken.None);
        Assert.Equal(2, tags.Count);
        Assert.All(tags, tag => Assert.True(tag.IsSystem));
        Assert.Equal(2, searches.Count);
        Assert.All(searches, search => Assert.True(search.IsSystem));
        Assert.Equal([SystemTags.InboxId], searches.Single(search => search.Id == SystemTags.InboxSearchId).RequiredTagIds);
        Assert.Empty(await Repository.GetResourcesAsync(CancellationToken.None));
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Collections';";
        Assert.Equal(0L, await check.ExecuteScalarAsync());
    }

    [Fact]
    public async Task TagsKeepIdentityOnRenameAndCannotBeDeletedWhileUsedBySearch()
    {
        var tag = new TagRecord(Guid.NewGuid(), "  Work  ", false);
        await Repository.SaveTagAsync(tag, CancellationToken.None);
        await Assert.ThrowsAsync<SqliteException>(() => Repository.SaveTagAsync(new TagRecord(Guid.NewGuid(), "work", false), CancellationToken.None));
        var search = NewSearch("Проекты", [tag.Id]);
        await Repository.SaveSearchAsync(search, CancellationToken.None);
        await Repository.SaveTagAsync(tag with { Name = "Работа" }, CancellationToken.None);
        Assert.Equal(tag.Id, (await Repository.GetSearchesAsync(CancellationToken.None)).Single(item => item.Id == search.Id).RequiredTagIds.Single());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.DeleteTagAsync(tag.Id, CancellationToken.None));
        Assert.Contains("Проекты", error.Message, StringComparison.Ordinal);
        await Repository.DeleteSearchTreeAsync(search.Id, CancellationToken.None);
        await Repository.DeleteTagAsync(tag.Id, CancellationToken.None);
        Assert.DoesNotContain(await Repository.GetTagsAsync(CancellationToken.None), item => item.Id == tag.Id);
    }

    [Fact]
    public async Task SearchTreeAllowsDuplicateNamesButRejectsCycleAndDeletesOnlySubtree()
    {
        var tag = new TagRecord(Guid.NewGuid(), "Тема", false);
        await Repository.SaveTagAsync(tag, CancellationToken.None);
        var root = NewSearch("Совпадение", [tag.Id]);
        var child = NewSearch("Совпадение", []) with { ParentId = root.Id };
        await Repository.SaveSearchAsync(root, CancellationToken.None);
        await Repository.SaveSearchAsync(child, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.SaveSearchAsync(root with { ParentId = child.Id }, CancellationToken.None));
        var resource = NewResource([tag.Id]);
        await Repository.SaveResourceAsync(resource, CancellationToken.None);
        await Repository.DeleteSearchTreeAsync(root.Id, CancellationToken.None);
        Assert.Equal(2, (await Repository.GetSearchesAsync(CancellationToken.None)).Count);
        Assert.Equal([tag.Id], (await Repository.GetResourcesAsync(CancellationToken.None)).Single().TagIds);
    }

    [Fact]
    public async Task RemovingTagAddsInboxOnlyToAffectedActiveResources()
    {
        var tag = new TagRecord(Guid.NewGuid(), "Проект", false);
        await Repository.SaveTagAsync(tag, CancellationToken.None);
        var affected = NewResource([tag.Id]);
        var manuallyUntagged = NewResource([]);
        var trashed = NewResource([tag.Id, SystemTags.TrashId]);
        await Repository.SaveResourceAsync(affected, CancellationToken.None);
        await Repository.SaveResourceAsync(manuallyUntagged, CancellationToken.None);
        await Repository.SaveResourceAsync(trashed, CancellationToken.None);
        await Repository.DeleteTagAsync(tag.Id, CancellationToken.None);
        var saved = await Repository.GetResourcesAsync(CancellationToken.None);
        Assert.Equal([SystemTags.InboxId], saved.Single(item => item.Id == affected.Id).TagIds);
        Assert.Empty(saved.Single(item => item.Id == manuallyUntagged.Id).TagIds);
        Assert.Equal([SystemTags.TrashId], saved.Single(item => item.Id == trashed.Id).TagIds);
    }

    [Fact]
    public async Task ResourceRoundTripsTagsAndVisualAndSystemSearchIsProtected()
    {
        var resource = NewResource([SystemTags.InboxId, SystemTags.TrashId]);
        await Repository.SaveResourceAsync(resource, CancellationToken.None);
        var saved = (await Repository.GetResourcesAsync(CancellationToken.None)).Single();
        Assert.Equal(resource.Visual, saved.Visual);
        Assert.Equal(resource.TagIds.Order(), saved.TagIds.Order());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.DeleteSearchTreeAsync(SystemTags.InboxSearchId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.DeleteTagAsync(SystemTags.InboxId, CancellationToken.None));
    }

    [Fact]
    public async Task LegacyWalDatabaseIsBackedUpBeforeEmptyWorkspaceIsCreated()
    {
        _provider?.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(DatabasePath);
        await using (var legacy = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await legacy.OpenAsync();
            await using var command = legacy.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE Legacy (Value TEXT); INSERT INTO Legacy VALUES('preserved');";
            await command.ExecuteNonQueryAsync();
        }
        _provider = CreateServices();
        await _provider.GetRequiredService<IApplicationInitializer>().InitializeAsync(CancellationToken.None);
        var backupPath = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(DatabasePath)!, "workspace.collections-backup-*.db"));
        await using var backup = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly");
        await backup.OpenAsync();
        await using var query = backup.CreateCommand();
        query.CommandText = "SELECT Value FROM Legacy;";
        Assert.Equal("preserved", await query.ExecuteScalarAsync());
        Assert.Empty(await Repository.GetResourcesAsync(CancellationToken.None));
        await _provider.GetRequiredService<IApplicationInitializer>().InitializeAsync(CancellationToken.None);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(DatabasePath)!, "workspace.collections-backup-*.db"));
    }

    private static SavedSearchRecord NewSearch(string name, IReadOnlyList<Guid> tags) =>
        new(Guid.NewGuid(), name, null, "\uE721", "#7EA7FF", 2, false, tags);

    private static TaggedResourceRecord NewResource(IReadOnlyList<Guid> tags) =>
        new(Guid.NewGuid(), "Ресурс", "Описание", StoredResourceKinds.File, "сейчас", "\uE8A5", "C:\\resource.txt",
            new ResourceVisualDescriptor(ResourceVisualMode.UserAsset, ResourceVisualKind.Image, "Data/Visuals/user/a.png", ResourceVisualOrigin.UserProvided, "hash"), tags);

    private string DatabasePath => Path.Combine(_root, "Data", "workspace.db");

    private ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppDataPathProvider>(new TestPathProvider(_root));
        services.AddInfrastructure();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class TestPathProvider(string path) : IAppDataPathProvider
    {
        public string GetLocalDataRoot() => path;
    }
}
