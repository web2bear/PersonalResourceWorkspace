using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Resources;
using PersonalResourceWorkspace.Domain.Resources;

namespace PersonalResourceWorkspace.Application.Tests.Resources;

public sealed class ResourceLaunchServiceTests
{
    [Fact]
    public async Task LaunchAsyncOpensHttpUriInBrowser()
    {
        var launcher = new FakeShellLauncher();
        var service = new ResourceLaunchService(launcher);

        await service.LaunchAsync(StoredResourceKinds.Web, "https://example.com/docs", CancellationToken.None);

        Assert.NotNull(launcher.LastUri);
        Assert.Equal("https://example.com/docs", launcher.LastUri.AbsoluteUri);
        Assert.Null(launcher.LastPath);
    }

    [Fact]
    public async Task LaunchAsyncNormalizesSchemeLessWebAddress()
    {
        var launcher = new FakeShellLauncher();
        var service = new ResourceLaunchService(launcher);

        await service.LaunchAsync(StoredResourceKinds.LegacyWeb, "example.com", CancellationToken.None);

        Assert.Equal("https://example.com/", launcher.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task LaunchAsyncRejectsNonHttpWebAddress()
    {
        var launcher = new FakeShellLauncher();
        var service = new ResourceLaunchService(launcher);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LaunchAsync(StoredResourceKinds.Web, "javascript:alert(1)", CancellationToken.None));

        Assert.Equal("Укажите корректную HTTP или HTTPS ссылку.", exception.Message);
        Assert.Null(launcher.LastUri);
    }

    [Fact]
    public async Task LaunchAsyncOpensFilePath()
    {
        var launcher = new FakeShellLauncher();
        var service = new ResourceLaunchService(launcher);

        await service.LaunchAsync(StoredResourceKinds.File, @"C:\docs\file.txt", CancellationToken.None);

        Assert.Equal(@"C:\docs\file.txt", launcher.LastPath);
        Assert.Null(launcher.LastUri);
    }

    [Fact]
    public void MapperNormalizesLegacyWebKind()
    {
        Assert.Equal(ResourceType.Web, ResourceTypeMapper.FromStoredKind(StoredResourceKinds.LegacyWeb));
        Assert.Equal(StoredResourceKinds.Web, ResourceTypeMapper.NormalizeStoredKind(StoredResourceKinds.LegacyWeb));
        Assert.Equal("\uE71B", ResourceTypeMapper.GlyphForStoredKind(StoredResourceKinds.Web));
    }

    [Theory]
    [InlineData(StoredResourceKinds.Application, ResourceType.Application)]
    [InlineData(StoredResourceKinds.Web, ResourceType.Web)]
    [InlineData(StoredResourceKinds.File, ResourceType.File)]
    [InlineData(StoredResourceKinds.Folder, ResourceType.Folder)]
    [InlineData(StoredResourceKinds.Note, ResourceType.TextSnippet)]
    [InlineData(StoredResourceKinds.Image, ResourceType.ImageSnippet)]
    [InlineData(StoredResourceKinds.Command, ResourceType.Command)]
    [InlineData(StoredResourceKinds.Collection, ResourceType.Collection)]
    public void MapperRoundTripsEveryResourceType(string storedKind, ResourceType expectedType)
    {
        Assert.Equal(expectedType, ResourceTypeMapper.FromStoredKind(storedKind));
        Assert.Equal(storedKind, ResourceTypeMapper.ToStoredKind(expectedType));
    }

    private sealed class FakeShellLauncher : IShellLauncher
    {
        public Uri? LastUri { get; private set; }

        public string? LastPath { get; private set; }

        public Task OpenUriAsync(Uri uri, CancellationToken cancellationToken)
        {
            LastUri = uri;
            return Task.CompletedTask;
        }

        public Task OpenPathAsync(string path, CancellationToken cancellationToken)
        {
            LastPath = path;
            return Task.CompletedTask;
        }
    }
}
