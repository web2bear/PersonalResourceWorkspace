using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Preferences;

namespace PersonalResourceWorkspace.Windows.Storage;

internal sealed class FileCollectionViewPreferenceStore(IAppDataPathProvider pathProvider)
    : ICollectionViewPreferenceStore
{
    private readonly string _settingsPath = Path.Combine(
        pathProvider.GetLocalDataRoot(),
        "Settings",
        "collection-view.txt");

    public async Task<CollectionViewMode> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return CollectionViewMode.Grid;
        }

        var value = await File.ReadAllTextAsync(_settingsPath, cancellationToken).ConfigureAwait(false);
        return CollectionViewModePreference.Parse(value.Trim());
    }

    public async Task SaveAsync(CollectionViewMode mode, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("The view settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            CollectionViewModePreference.Serialize(mode),
            cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
