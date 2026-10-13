namespace PersonalResourceWorkspace.Application.Abstractions;

public interface IClipboardTextWriter
{
    Task WriteTextAsync(string text, CancellationToken cancellationToken);
}
