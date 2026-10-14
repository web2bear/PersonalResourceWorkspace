using Microsoft.Extensions.DependencyInjection;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Models;
using PersonalResourceWorkspace.Domain.Resources;

namespace PersonalResourceWorkspace.Windows.Tests.Visuals;

public sealed class ResourceVisualCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prw-visual-cache-{Guid.NewGuid():N}");

    [Fact]
    public async Task CorruptCachedImageIsRebuiltFromSource()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "source.png");
        await File.WriteAllBytesAsync(source, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lXcAAAAASUVORK5CYII="));

        using var provider = new ServiceCollection()
            .AddSingleton<IAppDataPathProvider>(new TestPathProvider(_root))
            .AddWindowsPlatform()
            .BuildServiceProvider();
        var visuals = provider.GetRequiredService<IResourceVisualService>();
        var first = await visuals.ResolveAsync(
            new ResourceVisualRequest(Guid.NewGuid(), ResourceType.ImageSnippet, source, "", ResourceVisualDescriptor.Auto),
            false, CancellationToken.None);
        Assert.NotNull(first.AbsoluteAssetPath);

        await File.WriteAllBytesAsync(first.AbsoluteAssetPath, [1, 2, 3]);
        var second = await visuals.ResolveAsync(
            new ResourceVisualRequest(Guid.NewGuid(), ResourceType.ImageSnippet, source, "", first.Descriptor),
            false, CancellationToken.None);

        Assert.Equal(ResourceVisualOrigin.Payload, second.Descriptor.Origin);
        Assert.NotNull(second.AbsoluteAssetPath);
        Assert.True(new FileInfo(second.AbsoluteAssetPath).Length > 3);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class TestPathProvider(string root) : IAppDataPathProvider
    {
        public string GetLocalDataRoot() => root;
    }
}
