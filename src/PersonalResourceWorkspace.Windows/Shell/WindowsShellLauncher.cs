using System.Diagnostics;
using PersonalResourceWorkspace.Application.Abstractions;
using Windows.System;

namespace PersonalResourceWorkspace.Windows.Shell;

internal sealed class WindowsShellLauncher : IShellLauncher
{
    public async Task OpenUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await Launcher.LaunchUriAsync(uri))
        {
            return;
        }

        StartWithShell(uri.AbsoluteUri);
    }

    public Task OpenPathAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartWithShell(path);
        return Task.CompletedTask;
    }

    private static void StartWithShell(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
