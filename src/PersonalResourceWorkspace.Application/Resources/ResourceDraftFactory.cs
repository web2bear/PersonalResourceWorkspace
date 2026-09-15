using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Domain.Resources;

namespace PersonalResourceWorkspace.Application.Resources;

public static class ResourceDraftFactory
{
    public static ResourceDraft? TryCreate(ClipboardSnapshot snapshot)
    {
        var path = snapshot.Paths.FirstOrDefault(static candidate => !string.IsNullOrWhiteSpace(candidate));
        if (path is not null)
        {
            return FromPath(path);
        }

        if (snapshot.Uri is not null && TryFromUri(snapshot.Uri, out var fromUri))
        {
            return PreferHtmlTitle(fromUri, snapshot.Html);
        }

        if (TryParseText(snapshot.Text, out var fromText))
        {
            return PreferHtmlTitle(fromText, snapshot.Html);
        }

        if (TryParseHtml(snapshot.Html) is { } htmlDraft)
        {
            return htmlDraft;
        }

        if (string.IsNullOrWhiteSpace(snapshot.Text))
        {
            return null;
        }

        var text = snapshot.Text.Trim();
        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "Текстовая заметка";
        var title = firstLine.Length <= 60 ? firstLine : firstLine[..57] + "…";
        return new ResourceDraft(title, "Текстовый фрагмент", StoredResourceKinds.Note, text);
    }

    public static ResourceDraft? TryParseText(string? text) =>
        TryParseText(text, out var draft) ? draft : null;

    public static bool TryParseText(string? text, [NotNullWhen(true)] out ResourceDraft? draft)
    {
        draft = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = UnwrapQuotes(text);
        if (TryParseAddress(trimmed, requireExistingPath: false, out draft))
        {
            return true;
        }

        foreach (var url in ExtractHttpUrls(trimmed))
        {
            if (TryParseAddress(url, requireExistingPath: false, out draft))
            {
                return true;
            }
        }

        foreach (var token in EnumeratePathTokens(trimmed))
        {
            if (TryParseAddress(token, requireExistingPath: true, out draft))
            {
                return true;
            }
        }

        return false;
    }

    public static ResourceDraft FromPath(string path)
    {
        if (!TryFromPath(path, requireExisting: false, out var draft))
        {
            var address = NormalizePath(path.Trim());
            return new ResourceDraft(
                FileTitle(address),
                FileDescription(address, Directory.Exists(address)),
                KindForPath(address, Directory.Exists(address)),
                address);
        }

        return draft;
    }

    public static ResourceDraft FromUri(Uri uri)
    {
        if (TryFromUri(uri, out var draft))
        {
            return draft;
        }

        var isFile = uri.IsFile;
        var address = isFile ? uri.LocalPath : uri.AbsoluteUri;
        var title = isFile ? FileTitle(uri.LocalPath) : uri.Host;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = address;
        }

        return new ResourceDraft(
            title,
            isFile ? FileDescription(uri.LocalPath, Directory.Exists(uri.LocalPath)) : uri.AbsoluteUri,
            isFile ? StoredResourceKinds.File : StoredResourceKinds.Web,
            address);
    }

    private static bool TryFromUri(Uri uri, [NotNullWhen(true)] out ResourceDraft? draft)
    {
        if (uri.IsFile)
        {
            return TryFromPath(uri.LocalPath, requireExisting: false, out draft);
        }

        if (WebUri.TryCreate(uri.AbsoluteUri, out var webUri))
        {
            draft = FromWeb(webUri);
            return true;
        }

        draft = null!;
        return false;
    }

    private static ResourceDraft? TryParseHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html) || !TryGetAnchor(html, out var href, out var title))
        {
            return null;
        }

        if (!TryParseAddress(href, requireExistingPath: false, out var draft))
        {
            return null;
        }

        return PreferTitle(draft, title);
    }

    private static ResourceDraft PreferHtmlTitle(ResourceDraft draft, string? html)
    {
        if (TryGetAnchor(html, out _, out var title))
        {
            return PreferTitle(draft, title);
        }

        return draft;
    }

    private static ResourceDraft PreferTitle(ResourceDraft draft, string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || IsSameAddress(title, draft.Address))
        {
            return draft;
        }

        return draft with { Title = title.Trim() };
    }

    private static ResourceDraft FromWeb(WebUri webUri) => new(
        string.IsNullOrWhiteSpace(webUri.Value.Host) ? webUri.AbsoluteUri : webUri.Value.Host,
        webUri.AbsoluteUri,
        StoredResourceKinds.Web,
        webUri.AbsoluteUri);

    private static bool TryParseAddress(string text, bool requireExistingPath, [NotNullWhen(true)] out ResourceDraft? draft)
    {
        var candidate = UnwrapQuotes(text);
        if (candidate.Length == 0)
        {
            draft = null!;
            return false;
        }

        if (LooksLikeFilePath(candidate))
        {
            return TryFromPath(candidate, requireExistingPath, out draft);
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return TryFromPath(uri.LocalPath, requireExistingPath, out draft);
        }

        if (WebUri.TryCreate(candidate, out var webUri))
        {
            draft = FromWeb(webUri);
            return true;
        }

        draft = null!;
        return false;
    }

    private static bool TryFromPath(string path, bool requireExisting, [NotNullWhen(true)] out ResourceDraft? draft)
    {
        var address = NormalizePath(UnwrapQuotes(path));
        var isFolder = Directory.Exists(address);
        var exists = isFolder || File.Exists(address);
        if (requireExisting && !exists)
        {
            draft = null!;
            return false;
        }

        if (!exists && !LooksLikeFilePath(address))
        {
            draft = null!;
            return false;
        }

        draft = new ResourceDraft(
            FileTitle(address),
            FileDescription(address, isFolder),
            KindForPath(address, isFolder),
            address);
        return true;
    }

    private static string KindForPath(string path, bool isFolder)
    {
        if (isFolder)
        {
            return StoredResourceKinds.Folder;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".exe" or ".lnk" or ".appref-ms" => StoredResourceKinds.Application,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".tif" or ".tiff" => StoredResourceKinds.Image,
            _ => StoredResourceKinds.File,
        };
    }

    private static string FileTitle(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static string FileDescription(string path, bool isFolder)
    {
        if (isFolder)
        {
            return "Папка из файловой системы";
        }

        var extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrEmpty(extension) ? "Файл из файловой системы" : $"{extension} · файл из Проводника";
    }

    private static bool LooksLikeFilePath(string text)
    {
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return Path.IsPathRooted(text) && !text.Contains("://", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
        catch (NotSupportedException)
        {
            return path;
        }
        catch (PathTooLongException)
        {
            return path;
        }
    }

    private static string UnwrapQuotes(string text)
    {
        var trimmed = text.Trim().Trim('\uFEFF');
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    private static IEnumerable<string> ExtractHttpUrls(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var http = text.IndexOf("http://", start, StringComparison.OrdinalIgnoreCase);
            var https = text.IndexOf("https://", start, StringComparison.OrdinalIgnoreCase);
            var index = MinIndex(http, https);
            if (index < 0)
            {
                yield break;
            }

            var end = index;
            while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ('<' or '>' or '"' or '\''))
            {
                end++;
            }

            var url = text[index..end].TrimEnd('.', ',', ';', ':', ')', ']', '}', '!', '?');
            if (url.Length > 0)
            {
                yield return url;
            }

            start = Math.Max(end, index + 1);
        }
    }

    private static IEnumerable<string> EnumeratePathTokens(string text)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var unquoted = UnwrapQuotes(line);
            if (LooksLikeFilePath(unquoted))
            {
                yield return unquoted;
            }

            foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = UnwrapQuotes(token.TrimEnd('.', ',', ';', ':', ')', ']', '}'));
                if (LooksLikeFilePath(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static bool TryGetAnchor(string? html, out string href, out string title)
    {
        href = string.Empty;
        title = string.Empty;
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        var hrefIndex = html.IndexOf("href=", StringComparison.OrdinalIgnoreCase);
        if (hrefIndex < 0)
        {
            return false;
        }

        var valueStart = hrefIndex + 5;
        while (valueStart < html.Length && char.IsWhiteSpace(html[valueStart]))
        {
            valueStart++;
        }

        if (valueStart >= html.Length)
        {
            return false;
        }

        int valueEnd;
        if (html[valueStart] is '"' or '\'')
        {
            var quote = html[valueStart];
            valueStart++;
            valueEnd = html.IndexOf(quote, valueStart);
            if (valueEnd < 0)
            {
                return false;
            }
        }
        else
        {
            valueEnd = valueStart;
            while (valueEnd < html.Length && !char.IsWhiteSpace(html[valueEnd]) && html[valueEnd] != '>')
            {
                valueEnd++;
            }
        }

        href = DecodeHtml(html[valueStart..valueEnd].Trim());
        if (href.Length == 0)
        {
            return false;
        }

        var tagEnd = html.IndexOf('>', valueEnd);
        var close = html.IndexOf("</a", tagEnd < 0 ? valueEnd : tagEnd, StringComparison.OrdinalIgnoreCase);
        if (tagEnd >= 0 && close > tagEnd)
        {
            title = StripTags(html[(tagEnd + 1)..close]);
        }

        return true;
    }

    private static string StripTags(string html)
    {
        var builder = new StringBuilder(html.Length);
        var insideTag = false;
        foreach (var character in html)
        {
            if (character == '<')
            {
                insideTag = true;
            }
            else if (character == '>')
            {
                insideTag = false;
            }
            else if (!insideTag)
            {
                builder.Append(character);
            }
        }

        return DecodeHtml(builder.ToString()).Trim();
    }

    private static string DecodeHtml(string value)
    {
        var decoded = WebUtility.HtmlDecode(value).Replace('\u00A0', ' ');
        return decoded.Trim();
    }

    private static bool IsSameAddress(string title, string address) =>
        string.Equals(UnwrapQuotes(title), address, StringComparison.OrdinalIgnoreCase) ||
        (WebUri.TryCreate(title, out var titleUri) &&
         WebUri.TryCreate(address, out var addressUri) &&
         string.Equals(titleUri.AbsoluteUri, addressUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));

    private static int MinIndex(int first, int second)
    {
        if (first < 0)
        {
            return second;
        }

        if (second < 0)
        {
            return first;
        }

        return Math.Min(first, second);
    }
}
