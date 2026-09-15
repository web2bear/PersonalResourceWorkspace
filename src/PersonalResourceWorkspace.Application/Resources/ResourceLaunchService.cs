using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Domain.Resources;

namespace PersonalResourceWorkspace.Application.Resources;

public sealed class ResourceLaunchService(IShellLauncher shellLauncher)
{
    public async Task LaunchAsync(string kind, string address, CancellationToken cancellationToken)
    {
        var type = ResourceTypeMapper.FromStoredKind(kind);
        switch (type)
        {
            case ResourceType.Web:
                await LaunchWebAsync(address, cancellationToken);
                break;
            case ResourceType.Application:
            case ResourceType.File:
            case ResourceType.Folder:
            case ResourceType.TextSnippet:
            case ResourceType.ImageSnippet:
            case ResourceType.Command:
            case ResourceType.Collection:
                await LaunchPathAsync(address, cancellationToken);
                break;
            default:
                ResourceType unreachable = type;
                throw new InvalidOperationException($"Unknown resource type: {unreachable}");
        }
    }

    private async Task LaunchWebAsync(string address, CancellationToken cancellationToken)
    {
        if (!WebUri.TryCreate(address, out var webUri))
        {
            throw new InvalidOperationException("Укажите корректную HTTP или HTTPS ссылку.");
        }

        await shellLauncher.OpenUriAsync(webUri.Value, cancellationToken);
    }

    private async Task LaunchPathAsync(string address, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException("У ресурса не указан адрес");
        }

        await shellLauncher.OpenPathAsync(address.Trim(), cancellationToken);
    }
}
