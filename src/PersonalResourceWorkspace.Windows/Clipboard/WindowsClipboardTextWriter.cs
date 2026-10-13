using PersonalResourceWorkspace.Application.Abstractions;
using Windows.ApplicationModel.DataTransfer;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace PersonalResourceWorkspace.Windows.Clipboard;

internal sealed class WindowsClipboardTextWriter : IClipboardTextWriter
{
    public Task WriteTextAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = new DataPackage();
        content.SetText(text);
        cancellationToken.ThrowIfCancellationRequested();
        WinClipboard.SetContent(content);
        return Task.CompletedTask;
    }
}
