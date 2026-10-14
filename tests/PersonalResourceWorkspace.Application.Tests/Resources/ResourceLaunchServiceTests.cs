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
        var service = new ResourceLaunchService(launcher, new FakeClipboardTextWriter());

        await service.LaunchAsync(StoredResourceKinds.Web, "https://example.com/docs", CancellationToken.None);

        Assert.NotNull(launcher.LastUri);
        Assert.Equal("https://example.com/docs", launcher.LastUri.AbsoluteUri);
        Assert.Null(launcher.LastPath);
    }

    [Fact]
    public async Task LaunchAsyncNormalizesSchemeLessWebAddress()
    {
        var launcher = new FakeShellLauncher();
        var service = new ResourceLaunchService(launcher, new FakeClipboardTextWriter());

        await service.LaunchAsync(StoredResourceKinds.LegacyWeb, "example.com", CancellationToken.None);

        Assert.Equal("https://example.com/", launcher.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task LaunchAsyncRejectsNonHttpWebAddress()
    {
        var launcher = new FakeShellLauncher();
        var service = new ResourceLaunchService(launcher, new FakeClipboardTextWriter());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LaunchAsync(StoredResourceKinds.Web, "javascript:alert(1)", CancellationToken.None));

        Assert.Equal("Укажите корректную HTTP или HTTPS ссылку.", exception.Message);
        Assert.Null(launcher.LastUri);
    }

    [Fact]
    public async Task LaunchAsyncOpensFilePath()
    {
        var launcher = new FakeShellLauncher();
        var service = new ResourceLaunchService(launcher, new FakeClipboardTextWriter());

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

    [Fact]
    public async Task LaunchAsyncCopiesNoteExactlyOnceWithoutOpeningShell()
    {
        var launcher = new FakeShellLauncher();
        var clipboard = new FakeClipboardTextWriter();
        var service = new ResourceLaunchService(launcher, clipboard);
        const string content = "  первая строка\r\nвторая строка  ";

        await service.LaunchAsync(StoredResourceKinds.Note, content, CancellationToken.None);

        Assert.Equal(content, clipboard.LastText);
        Assert.Equal(1, clipboard.WriteCount);
        Assert.Null(launcher.LastPath);
        Assert.Null(launcher.LastUri);
    }

    [Fact]
    public async Task LaunchAsyncDoesNotCopyCancelledNote()
    {
        var clipboard = new FakeClipboardTextWriter();
        var service = new ResourceLaunchService(new FakeShellLauncher(), clipboard);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.LaunchAsync(StoredResourceKinds.Note, "text", cancellation.Token));

        Assert.Equal(0, clipboard.WriteCount);
    }

    [Fact]
    public async Task LaunchAsyncPropagatesClipboardErrorWithoutShellFallback()
    {
        var launcher = new FakeShellLauncher();
        var clipboard = new FakeClipboardTextWriter { Error = new InvalidOperationException("clipboard unavailable") };
        var service = new ResourceLaunchService(launcher, clipboard);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LaunchAsync(StoredResourceKinds.Note, "text", CancellationToken.None));

        Assert.Equal(1, clipboard.WriteCount);
        Assert.Null(launcher.LastPath);
        Assert.Null(launcher.LastUri);
    }

    [Theory]
    [InlineData(StoredResourceKinds.Application, ResourceType.Application)]
    [InlineData(StoredResourceKinds.Web, ResourceType.Web)]
    [InlineData(StoredResourceKinds.File, ResourceType.File)]
    [InlineData(StoredResourceKinds.Folder, ResourceType.Folder)]
    [InlineData(StoredResourceKinds.Note, ResourceType.TextSnippet)]
    [InlineData(StoredResourceKinds.Image, ResourceType.ImageSnippet)]
    [InlineData(StoredResourceKinds.Command, ResourceType.Command)]
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

    private sealed class FakeClipboardTextWriter : IClipboardTextWriter
    {
        public string? LastText { get; private set; }
        public int WriteCount { get; private set; }
        public Exception? Error { get; init; }

        public Task WriteTextAsync(string text, CancellationToken cancellationToken)
        {
            WriteCount++;
            if (Error is not null) throw Error;
            LastText = text;
            return Task.CompletedTask;
        }
    }
}
