using PersonalResourceWorkspace.Application.Abstractions;
using Windows.ApplicationModel.DataTransfer;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace PersonalResourceWorkspace.Windows.Clipboard;

internal sealed class WindowsClipboardService : IClipboardService
{
    public async Task<ClipboardSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DataPackageView content;
        try
        {
            content = WinClipboard.GetContent();
        }
        catch (Exception)
        {
            return ClipboardSnapshot.Empty;
        }

        var paths = await ReadPathsAsync(content);
        var uri = await ReadUriAsync(content);
        var text = await ReadTextAsync(content);
        var html = await ReadHtmlAsync(content);
        cancellationToken.ThrowIfCancellationRequested();

        if (paths.Count == 0 && uri is null && text is null && html is null)
        {
            return ClipboardSnapshot.Empty;
        }

        return new ClipboardSnapshot
        {
            Text = text,
            Html = html,
            Uri = uri,
            Paths = paths,
        };
    }

    private static async Task<IReadOnlyList<string>> ReadPathsAsync(DataPackageView content)
    {
        if (!content.Contains(StandardDataFormats.StorageItems))
        {
            return [];
        }

        try
        {
            var items = await content.GetStorageItemsAsync();
            return items
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static async Task<Uri?> ReadUriAsync(DataPackageView content)
    {
        if (content.Contains(StandardDataFormats.WebLink))
        {
            var webLink = await TryReadAsync(async () => await content.GetWebLinkAsync());
            if (webLink is not null)
            {
                return webLink;
            }
        }

        if (content.Contains(StandardDataFormats.Uri))
        {
            return await TryReadAsync(async () => await content.GetUriAsync());
        }

        return null;
    }

    private static Task<string?> ReadTextAsync(DataPackageView content) =>
        content.Contains(StandardDataFormats.Text)
            ? TryReadAsync(async () => await content.GetTextAsync())
            : Task.FromResult<string?>(null);

    private static Task<string?> ReadHtmlAsync(DataPackageView content) =>
        content.Contains(StandardDataFormats.Html)
            ? TryReadAsync(async () => await content.GetHtmlFormatAsync())
            : Task.FromResult<string?>(null);

    private static async Task<T?> TryReadAsync<T>(Func<Task<T>> read)
        where T : class
    {
        try
        {
            return await read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
