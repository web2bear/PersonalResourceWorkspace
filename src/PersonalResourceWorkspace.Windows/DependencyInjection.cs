using Microsoft.Extensions.DependencyInjection;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Windows.Clipboard;
using PersonalResourceWorkspace.Windows.Shell;
using PersonalResourceWorkspace.Windows.Storage;
using PersonalResourceWorkspace.Windows.Visuals;

namespace PersonalResourceWorkspace.Windows;

public static class DependencyInjection
{
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IAppDataPathProvider, UnpackagedAppDataPathProvider>();
        services.AddSingleton<ICollectionViewPreferenceStore, FileCollectionViewPreferenceStore>();
        services.AddSingleton<IClipboardService, WindowsClipboardService>();
        services.AddSingleton<IShellLauncher, WindowsShellLauncher>();
        services.AddSingleton<IResourceVisualService, WindowsResourceVisualService>();
        return services;
    }
}
