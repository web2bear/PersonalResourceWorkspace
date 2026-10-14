using PersonalResourceWorkspace.Application.Preferences;

namespace PersonalResourceWorkspace.Application.Abstractions;

public interface IResourceViewPreferenceStore
{
    Task<ResourceViewMode> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ResourceViewMode mode, CancellationToken cancellationToken);
}
