namespace PersonalResourceWorkspace.Application.Abstractions;

public interface IClipboardService
{
    Task<ClipboardSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public sealed class ClipboardSnapshot
{
    public static ClipboardSnapshot Empty { get; } = new();

    public string? Text { get; init; }

    public string? Html { get; init; }

    public Uri? Uri { get; init; }

    public IReadOnlyList<string> Paths { get; init; } = [];
}
