using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Preferences;

namespace PersonalResourceWorkspace.Infrastructure.Persistence;

internal sealed class SqliteResourceViewPreferenceStore(IUserPreferenceStore preferences) : IResourceViewPreferenceStore
{
    public async Task<ResourceViewMode> LoadAsync(CancellationToken cancellationToken) =>
        ResourceViewModePreference.Parse(await preferences.GetAsync("resource-view", cancellationToken).ConfigureAwait(false));

    public Task SaveAsync(ResourceViewMode mode, CancellationToken cancellationToken) =>
        preferences.SetAsync("resource-view", ResourceViewModePreference.Serialize(mode), cancellationToken);
}
