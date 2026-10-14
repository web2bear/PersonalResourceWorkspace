using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Preferences;

namespace PersonalResourceWorkspace.Infrastructure.Persistence;

internal sealed class SqliteCollectionViewPreferenceStore(IUserPreferenceStore preferences) : ICollectionViewPreferenceStore
{
    public async Task<CollectionViewMode> LoadAsync(CancellationToken cancellationToken) =>
        CollectionViewModePreference.Parse(await preferences.GetAsync("collection-view", cancellationToken).ConfigureAwait(false));

    public Task SaveAsync(CollectionViewMode mode, CancellationToken cancellationToken) =>
        preferences.SetAsync("collection-view", CollectionViewModePreference.Serialize(mode), cancellationToken);
}
