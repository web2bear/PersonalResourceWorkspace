namespace PersonalResourceWorkspace.Application.Abstractions;

public interface IShellLauncher
{
    Task OpenUriAsync(Uri uri, CancellationToken cancellationToken);
    Task OpenPathAsync(string path, CancellationToken cancellationToken);
}
