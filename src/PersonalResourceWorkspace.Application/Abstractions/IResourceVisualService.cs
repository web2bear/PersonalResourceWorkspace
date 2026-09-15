using PersonalResourceWorkspace.Application.Models;

namespace PersonalResourceWorkspace.Application.Abstractions;

public interface IResourceVisualService
{
    Task<ResourceVisualResolution> ResolveAsync(
        ResourceVisualRequest request,
        bool forceRefresh,
        CancellationToken cancellationToken);

    Task<ResourceVisualDescriptor> ImportUserAssetAsync(
        string sourcePath,
        CancellationToken cancellationToken);

    string? GetAbsoluteAssetPath(string? relativeAssetPath);
}
