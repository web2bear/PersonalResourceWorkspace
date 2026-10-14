using Microsoft.Extensions.DependencyInjection;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Models;
using PersonalResourceWorkspace.Domain.Resources;

namespace PersonalResourceWorkspace.Windows.Tests.Visuals;

public sealed class ImageAccessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prw-image-access-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnpackagedApplicationCanDecodeFilesThroughFileSystemAccess(bool userAsset)
    {
        Directory.CreateDirectory(_root);
        var imagePath = Path.Combine(_root, "preview.png");
        await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1sAAAAASUVORK5CYII="));
        var services = new ServiceCollection();
        services.AddSingleton<IAppDataPathProvider>(new TestPaths(_root));
        services.AddWindowsPlatform();
        using var provider = services.BuildServiceProvider();
        var visuals = provider.GetRequiredService<IResourceVisualService>();
        var descriptor = userAsset
            ? await visuals.ImportUserAssetAsync(imagePath, CancellationToken.None)
            : ResourceVisualDescriptor.Auto;
        var result = await visuals.ResolveAsync(new ResourceVisualRequest(Guid.NewGuid(), ResourceType.ImageSnippet,
            imagePath, "", descriptor), false, CancellationToken.None);
        Assert.Equal(ResourceVisualKind.Image, result.Descriptor.Kind);
        Assert.NotNull(result.AbsoluteAssetPath);
        Assert.True(File.Exists(result.AbsoluteAssetPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class TestPaths(string root) : IAppDataPathProvider
    {
        public string GetLocalDataRoot() => root;
    }
}
