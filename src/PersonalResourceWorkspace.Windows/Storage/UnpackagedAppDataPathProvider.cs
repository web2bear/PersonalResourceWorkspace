using PersonalResourceWorkspace.Application.Abstractions;

namespace PersonalResourceWorkspace.Windows.Storage;

internal sealed class UnpackagedAppDataPathProvider : IAppDataPathProvider
{
    public string GetLocalDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PersonalResourceWorkspace");
    }
}
