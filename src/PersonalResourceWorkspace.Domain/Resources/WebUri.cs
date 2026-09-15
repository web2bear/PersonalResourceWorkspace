using System.Diagnostics.CodeAnalysis;

namespace PersonalResourceWorkspace.Domain.Resources;

public sealed class WebUri
{
    private WebUri(Uri value) => Value = value;

    public Uri Value { get; }

    public string AbsoluteUri => Value.AbsoluteUri;

    public static bool TryCreate(string? input, [NotNullWhen(true)] out WebUri? webUri)
    {
        webUri = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        if (!TryCreateAbsoluteHttpUri(text, out var uri) &&
            !(LooksLikeHost(text) && TryCreateAbsoluteHttpUri("https://" + text, out uri)))
        {
            return false;
        }

        if (uri is null || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        webUri = new WebUri(uri);
        return true;
    }

    private static bool LooksLikeHost(string text)
    {
        if (text.Contains("://", StringComparison.Ordinal) ||
            text.Contains('\\', StringComparison.Ordinal) ||
            Path.IsPathRooted(text))
        {
            return false;
        }

        var host = text.Split('/', 2)[0].Split('?', 2)[0].Split('#', 2)[0];
        var hostWithoutPort = host.Split(':', 2)[0];
        return hostWithoutPort.Contains('.', StringComparison.Ordinal) ||
               string.Equals(hostWithoutPort, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCreateAbsoluteHttpUri(string text, [NotNullWhen(true)] out Uri? uri)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        uri = null;
        return false;
    }
}
