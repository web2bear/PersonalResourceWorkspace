using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Collections;
using PersonalResourceWorkspace.Application.Models;

namespace PersonalResourceWorkspace.Infrastructure.Tests.Persistence;

public sealed class SqliteCollectionRepositoryTests : IAsyncLifetime
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), $"prw-collections-{Guid.NewGuid():N}");
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
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task InitializeSeedsDefaultCollections()
    {
        var collections = await Repository.GetAllAsync(CancellationToken.None);

        Assert.Equal(6, collections.Count);
        Assert.Contains(collections, collection => collection.Name == "Входящие" && collection.IsDefault);
        Assert.Contains(collections, collection => collection.Name == "Работа");
        Assert.Contains(collections, collection => collection.Name == SystemCollections.TrashName && collection.IsTrash);
    }

    [Fact]
    public async Task SaveAsyncCreatesCollection()
    {
        var created = new CollectionRecord(Guid.NewGuid(), "Архив", "\uE8B7", "#6D5CE7", 10, false);

        await Repository.SaveAsync(created, CancellationToken.None);

        var collections = await Repository.GetAllAsync(CancellationToken.None);
        Assert.Contains(collections, collection => collection.Id == created.Id && collection.Name == "Архив" && collection.Glyph == "\uE8B7");
    }

    [Fact]
    public async Task SaveAsyncRenamesCollectionAndUpdatesResources()
    {
        var work = (await Repository.GetAllAsync(CancellationToken.None)).Single(collection => collection.Name == "Работа");
        var resource = new ResourceRecord(Guid.NewGuid(), "Документ", "файл", "Файл", work.Name, "сейчас", "\uE8A5", false, "C:\\doc.txt", ResourceVisualDescriptor.Auto);
        await Resources.SaveAsync(resource, CancellationToken.None);

        await Repository.SaveAsync(work with { Name = "Проекты" }, CancellationToken.None);

        var updated = (await Resources.GetAllAsync(CancellationToken.None)).Single(item => item.Id == resource.Id);
        Assert.Equal("Проекты", updated.Collection);
        Assert.Contains(await Repository.GetAllAsync(CancellationToken.None), collection => collection.Id == work.Id && collection.Name == "Проекты");
    }

    [Fact]
    public async Task DeleteAsyncMovesResourcesToFallbackCollection()
    {
        var collections = await Repository.GetAllAsync(CancellationToken.None);
        var work = collections.Single(collection => collection.Name == "Работа");
        var inbox = collections.Single(collection => collection.IsDefault);
        var resource = new ResourceRecord(Guid.NewGuid(), "Документ", "файл", "Файл", work.Name, "сейчас", "\uE8A5", false, "C:\\doc.txt", ResourceVisualDescriptor.Auto);
        await Resources.SaveAsync(resource, CancellationToken.None);

        await Repository.DeleteAsync(work.Id, inbox.Id, CancellationToken.None);

        var updated = (await Resources.GetAllAsync(CancellationToken.None)).Single(item => item.Id == resource.Id);
        Assert.Equal(inbox.Name, updated.Collection);
        Assert.DoesNotContain(await Repository.GetAllAsync(CancellationToken.None), collection => collection.Id == work.Id);
    }

    [Fact]
    public async Task DeleteAsyncRejectsDefaultCollection()
    {
        var collections = await Repository.GetAllAsync(CancellationToken.None);
        var work = collections.Single(collection => collection.Name == "Работа");
        var inbox = collections.Single(collection => collection.IsDefault);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Repository.DeleteAsync(inbox.Id, work.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsyncRejectsTrashCollection()
    {
        var collections = await Repository.GetAllAsync(CancellationToken.None);
        var trash = collections.Single(collection => collection.IsTrash);
        var inbox = collections.Single(collection => collection.IsDefault);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Repository.DeleteAsync(trash.Id, inbox.Id, CancellationToken.None));

        Assert.Contains(await Repository.GetAllAsync(CancellationToken.None), collection => collection.Id == trash.Id);
    }

    private ICollectionRepository Repository => _provider!.GetRequiredService<ICollectionRepository>();

    private IResourceRepository Resources => _provider!.GetRequiredService<IResourceRepository>();

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
