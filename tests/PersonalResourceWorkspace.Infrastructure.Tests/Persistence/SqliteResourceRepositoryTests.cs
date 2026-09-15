using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Models;
using PersonalResourceWorkspace.Application.Resources;

namespace PersonalResourceWorkspace.Infrastructure.Tests.Persistence;

public sealed class SqliteResourceRepositoryTests : IAsyncLifetime
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), $"prw-resources-{Guid.NewGuid():N}");
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
    public async Task SaveAsyncRoundTripsWebLink()
    {
        var inbox = (await Collections.GetAllAsync(CancellationToken.None)).Single(collection => collection.IsDefault);
        var resource = new ResourceRecord(
            Guid.NewGuid(),
            "Документация",
            "https://learn.microsoft.com",
            StoredResourceKinds.Web,
            inbox.Name,
            "сейчас",
            ResourceTypeMapper.GlyphForStoredKind(StoredResourceKinds.Web),
            true,
            "https://learn.microsoft.com/",
            new ResourceVisualDescriptor(
                ResourceVisualMode.UserAsset,
                ResourceVisualKind.Image,
                "Data/Visuals/user/sample.png",
                ResourceVisualOrigin.UserProvided,
                "sample-hash"));

        await Resources.SaveAsync(resource, CancellationToken.None);

        var saved = (await Resources.GetAllAsync(CancellationToken.None)).Single(item => item.Id == resource.Id);
        Assert.Equal(StoredResourceKinds.Web, saved.Kind);
        Assert.Equal("https://learn.microsoft.com/", saved.Address);
        Assert.Equal("\uE71B", saved.Glyph);
        Assert.True(saved.IsFavorite);
        Assert.Equal(resource.Visual, saved.Visual);
    }

    [Fact]
    public async Task DeleteAsyncRemovesResource()
    {
        var inbox = (await Collections.GetAllAsync(CancellationToken.None)).Single(collection => collection.IsDefault);
        var resource = new ResourceRecord(
            Guid.NewGuid(),
            "Черновик",
            "временный файл",
            StoredResourceKinds.File,
            inbox.Name,
            "сейчас",
            ResourceTypeMapper.GlyphForStoredKind(StoredResourceKinds.File),
            false,
            "C:\\draft.txt",
            ResourceVisualDescriptor.Auto);
        await Resources.SaveAsync(resource, CancellationToken.None);

        await Resources.DeleteAsync(resource.Id, CancellationToken.None);

        Assert.DoesNotContain(await Resources.GetAllAsync(CancellationToken.None), item => item.Id == resource.Id);
    }

    [Fact]
    public async Task DeleteByCollectionAsyncRemovesOnlyThatCollection()
    {
        var collections = await Collections.GetAllAsync(CancellationToken.None);
        var inbox = collections.Single(collection => collection.IsDefault);
        var trash = collections.Single(collection => collection.IsTrash);
        var kept = new ResourceRecord(
            Guid.NewGuid(),
            "Оставить",
            "файл",
            StoredResourceKinds.File,
            inbox.Name,
            "сейчас",
            ResourceTypeMapper.GlyphForStoredKind(StoredResourceKinds.File),
            false,
            "C:\\keep.txt",
            ResourceVisualDescriptor.Auto);
        var trashed = new ResourceRecord(
            Guid.NewGuid(),
            "Удалить",
            "файл",
            StoredResourceKinds.File,
            trash.Name,
            "сейчас",
            ResourceTypeMapper.GlyphForStoredKind(StoredResourceKinds.File),
            false,
            "C:\\gone.txt",
            ResourceVisualDescriptor.Fallback);
        await Resources.SaveAsync(kept, CancellationToken.None);
        await Resources.SaveAsync(trashed, CancellationToken.None);

        await Resources.DeleteByCollectionAsync(trash.Name, CancellationToken.None);

        var remaining = await Resources.GetAllAsync(CancellationToken.None);
        Assert.Contains(remaining, item => item.Id == kept.Id);
        Assert.DoesNotContain(remaining, item => item.Id == trashed.Id);
    }

    private ICollectionRepository Collections => _provider!.GetRequiredService<ICollectionRepository>();

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
