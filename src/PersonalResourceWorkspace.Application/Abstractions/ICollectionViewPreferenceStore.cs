using PersonalResourceWorkspace.Application.Preferences;

namespace PersonalResourceWorkspace.Application.Abstractions;

public interface ICollectionViewPreferenceStore
{
    Task<CollectionViewMode> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(CollectionViewMode mode, CancellationToken cancellationToken);
}
