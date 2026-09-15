using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Models;
using PersonalResourceWorkspace.Domain.Resources;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace PersonalResourceWorkspace.Windows.Visuals;

internal sealed class WindowsResourceVisualService : IResourceVisualService, IDisposable
{
    private const long MaximumUserAssetBytes = 20 * 1024 * 1024;
    private const int MaximumRemoteAssetBytes = 10 * 1024 * 1024;
    private const int MaximumHtmlBytes = 512 * 1024;
    private const string ShellVisualCacheVersion = "shell-visual-v3";
    private const byte VisibleIconAlphaThreshold = 80;
    private static readonly string[] SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico", ".tif", ".tiff"];

    private readonly IAppDataPathProvider _pathProvider;
    private readonly HttpClient _httpClient;

    public WindowsResourceVisualService(IAppDataPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PersonalResourceWorkspace", "1.0"));
    }

    public async Task<ResourceVisualResolution> ResolveAsync(
        ResourceVisualRequest request,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (request.Descriptor.Mode == ResourceVisualMode.TypeFallback)
        {
            return Fallback(ResourceVisualMode.TypeFallback);
        }

        if (request.Descriptor.Mode == ResourceVisualMode.UserAsset)
        {
            var userPath = GetAbsoluteAssetPath(request.Descriptor.AssetPath);
            if (userPath is not null && File.Exists(userPath) && await IsDecodableImageAsync(userPath, cancellationToken).ConfigureAwait(false))
            {
                return new ResourceVisualResolution(request.Descriptor, userPath, null, "Пользовательское изображение");
            }

            return new ResourceVisualResolution(request.Descriptor, null, null, "Пользовательский визуал недоступен — показан значок типа");
        }

        var fingerprint = CreateFingerprint(request.Type, request.Address, request.Description);
        var cachedPath = GetAbsoluteAssetPath(request.Descriptor.AssetPath);
        if (!forceRefresh &&
            request.Descriptor.Mode == ResourceVisualMode.Auto &&
            string.Equals(request.Descriptor.SourceFingerprint, fingerprint, StringComparison.Ordinal) &&
            cachedPath is not null &&
            File.Exists(cachedPath))
        {
            return new ResourceVisualResolution(request.Descriptor, cachedPath, null, SourceLabel(request.Descriptor.Origin));
        }

        try
        {
            switch (request.Type)
            {
                case ResourceType.Web:
                    return await ResolveWebAsync(request.Address, fingerprint, cancellationToken).ConfigureAwait(false)
                        ?? Fallback(ResourceVisualMode.Auto, fingerprint);
                case ResourceType.Application:
                case ResourceType.File:
                case ResourceType.Folder:
                case ResourceType.Command:
                    return await ResolveShellAsync(request.Type, request.Address, fingerprint, cancellationToken).ConfigureAwait(false)
                        ?? Fallback(ResourceVisualMode.Auto, fingerprint);
                case ResourceType.ImageSnippet:
                    return await ResolvePayloadImageAsync(request.Address, fingerprint, cancellationToken).ConfigureAwait(false)
                        ?? Fallback(ResourceVisualMode.Auto, fingerprint);
                case ResourceType.TextSnippet:
                    return new ResourceVisualResolution(
                        new ResourceVisualDescriptor(
                            ResourceVisualMode.Auto,
                            ResourceVisualKind.GeneratedPreview,
                            null,
                            ResourceVisualOrigin.Generated,
                            fingerprint),
                        null,
                        CreateTextPreview(request.Address, request.Description),
                        "Предпросмотр текста");
                case ResourceType.Collection:
                    return Fallback(ResourceVisualMode.Auto, fingerprint);
                default:
                    return Fallback(ResourceVisualMode.Auto, fingerprint);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or ArgumentException or COMException)
        {
            return Fallback(ResourceVisualMode.Auto, fingerprint);
        }
    }

    public async Task<ResourceVisualDescriptor> ImportUserAssetAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Выберите PNG, JPEG, BMP, GIF, WebP, TIFF или ICO файл.");
        }

        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new InvalidOperationException("Выбранное изображение больше недоступно.");
        }

        if (info.Length <= 0 || info.Length > MaximumUserAssetBytes)
        {
            throw new InvalidOperationException("Изображение должно быть меньше 20 МБ.");
        }

        if (!await IsDecodableImageAsync(fullPath, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Windows не удалось декодировать выбранное изображение.");
        }

        var hash = await HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var destination = Path.Combine(GetVisualRoot(), "user", $"{hash}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!File.Exists(destination))
        {
            File.Copy(fullPath, destination, overwrite: false);
        }

        return new ResourceVisualDescriptor(
            ResourceVisualMode.UserAsset,
            ResourceVisualKind.Image,
            ToRelativePath(destination),
            ResourceVisualOrigin.UserProvided,
            hash);
    }

    public string? GetAbsoluteAssetPath(string? relativeAssetPath)
    {
        if (string.IsNullOrWhiteSpace(relativeAssetPath))
        {
            return null;
        }

        var root = Path.GetFullPath(_pathProvider.GetLocalDataRoot());
        var candidate = Path.GetFullPath(Path.Combine(root, relativeAssetPath.Replace('/', Path.DirectorySeparatorChar)));
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<ResourceVisualResolution?> ResolvePayloadImageAsync(
        string address,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(address) || !await IsDecodableImageAsync(address, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var extension = Path.GetExtension(address).ToLowerInvariant();
        var destination = CachePath(fingerprint, extension);
        await CopyIfMissingAsync(address, destination, cancellationToken).ConfigureAwait(false);
        return Resolution(destination, ResourceVisualKind.Image, ResourceVisualOrigin.Payload, fingerprint, "Изображение ресурса");
    }

    private async Task<ResourceVisualResolution?> ResolveShellAsync(
        ResourceType type,
        string address,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var path = ResolveShellSourcePath(type, address);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        IStorageItem? item = null;
        if (Directory.Exists(path))
        {
            item = await StorageFolder.GetFolderFromPathAsync(path);
        }
        else if (File.Exists(path))
        {
            item = await StorageFile.GetFileFromPathAsync(path);
        }

        if (item is null)
        {
            return null;
        }

        using var thumbnail = item switch
        {
            StorageFile file => await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 512, ThumbnailOptions.ResizeThumbnail),
            StorageFolder folder => await folder.GetThumbnailAsync(ThumbnailMode.SingleItem, 512, ThumbnailOptions.ResizeThumbnail),
            _ => null,
        };
        if (thumbnail is null || thumbnail.Size == 0 || thumbnail.Size > MaximumRemoteAssetBytes)
        {
            return null;
        }

        var bytes = await ReadStreamAsync(thumbnail, (int)thumbnail.Size, cancellationToken).ConfigureAwait(false);
        var normalizedBytes = await NormalizeIconCanvasAsync(bytes, cancellationToken).ConfigureAwait(false);
        var destination = CachePath(fingerprint, ".png");
        await WriteValidatedImageAsync(destination, normalizedBytes, cancellationToken).ConfigureAwait(false);
        return Resolution(destination, ResourceVisualKind.Icon, ResourceVisualOrigin.WindowsShell, fingerprint, "Значок Windows");
    }

    private static async Task<byte[]> NormalizeIconCanvasAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);
        var pixelProvider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
        cancellationToken.ThrowIfCancellationRequested();

        var pixels = pixelProvider.DetachPixelData();
        var width = checked((int)decoder.PixelWidth);
        var height = checked((int)decoder.PixelHeight);
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                if (pixels[rowOffset + (x * 4) + 3] <= VisibleIconAlphaThreshold)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return bytes;
        }

        var contentWidth = maxX - minX + 1;
        var contentHeight = maxY - minY + 1;
        var padding = Math.Max(2, (int)Math.Ceiling(Math.Max(contentWidth, contentHeight) * 0.08));
        minX = Math.Max(0, minX - padding);
        minY = Math.Max(0, minY - padding);
        maxX = Math.Min(width - 1, maxX + padding);
        maxY = Math.Min(height - 1, maxY + padding);

        var croppedWidth = maxX - minX + 1;
        var croppedHeight = maxY - minY + 1;
        if (croppedWidth == width && croppedHeight == height)
        {
            return bytes;
        }

        var cropped = new byte[checked(croppedWidth * croppedHeight * 4)];
        for (var row = 0; row < croppedHeight; row++)
        {
            System.Buffer.BlockCopy(
                pixels,
                ((minY + row) * width + minX) * 4,
                cropped,
                row * croppedWidth * 4,
                croppedWidth * 4);
        }

        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            (uint)croppedWidth,
            (uint)croppedHeight,
            decoder.DpiX,
            decoder.DpiY,
            cropped);
        await encoder.FlushAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (output.Size > MaximumRemoteAssetBytes)
        {
            throw new InvalidDataException("Normalized icon exceeds the allowed size.");
        }

        using var reader = new DataReader(output.GetInputStreamAt(0));
        var loaded = await reader.LoadAsync((uint)output.Size);
        var normalized = new byte[loaded];
        reader.ReadBytes(normalized);
        return normalized;
    }

    private async Task<ResourceVisualResolution?> ResolveWebAsync(
        string address,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var pageUri) ||
            pageUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var candidates = new List<(Uri Uri, bool IsCover)>();
        try
        {
            var html = await DownloadHtmlAsync(pageUri, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(html))
            {
                var metadata = FindVisualMetadata(html, pageUri);
                if (metadata.Cover is not null)
                {
                    candidates.Add((metadata.Cover, true));
                }

                if (metadata.Icon is not null)
                {
                    candidates.Add((metadata.Icon, false));
                }
            }
        }
        catch (HttpRequestException)
        {
        }

        candidates.Add((new Uri(pageUri, "/favicon.ico"), false));
        foreach (var candidate in candidates.DistinctBy(static candidate => candidate.Uri.AbsoluteUri))
        {
            try
            {
                var bytes = await DownloadImageAsync(candidate.Uri, cancellationToken).ConfigureAwait(false);
                if (bytes is null)
                {
                    continue;
                }

                var candidateFingerprint = HashText($"{fingerprint}|{candidate.Uri.AbsoluteUri}");
                var destination = CachePath(candidateFingerprint, ".img");
                await WriteValidatedImageAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
                return Resolution(
                    destination,
                    candidate.IsCover ? ResourceVisualKind.Image : ResourceVisualKind.Icon,
                    ResourceVisualOrigin.RemoteMetadata,
                    fingerprint,
                    candidate.IsCover ? "Изображение сайта" : "Иконка сайта");
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or InvalidDataException or COMException)
            {
            }
        }

        return null;
    }

    private async Task<string?> DownloadHtmlAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode ||
            response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) != true ||
            response.Content.Headers.ContentLength > MaximumHtmlBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await ReadLimitedAsync(stream, MaximumHtmlBytes, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]?> DownloadImageAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode ||
            response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true ||
            response.Content.Headers.ContentLength > MaximumRemoteAssetBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ReadLimitedAsync(stream, MaximumRemoteAssetBytes, cancellationToken).ConfigureAwait(false);
    }

    private static (Uri? Cover, Uri? Icon) FindVisualMetadata(string html, Uri pageUri)
    {
        Uri? cover = null;
        Uri? icon = null;
        foreach (Match match in Regex.Matches(html, "<(?:meta|link)\\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
        {
            var tag = match.Value;
            var property = GetAttribute(tag, "property") ?? GetAttribute(tag, "name");
            if (cover is null &&
                property is not null &&
                property.Equals("og:image", StringComparison.OrdinalIgnoreCase) &&
                TryCreateHttpUri(pageUri, GetAttribute(tag, "content"), out var coverUri))
            {
                cover = coverUri;
            }

            var rel = GetAttribute(tag, "rel");
            if (icon is null &&
                rel?.Contains("icon", StringComparison.OrdinalIgnoreCase) == true &&
                TryCreateHttpUri(pageUri, GetAttribute(tag, "href"), out var iconUri))
            {
                icon = iconUri;
            }
        }

        return (cover, icon);
    }

    private static string? GetAttribute(string tag, string attribute)
    {
        var match = Regex.Match(
            tag,
            $"\\b{Regex.Escape(attribute)}\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(50));
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value.Trim()) : null;
    }

    private static bool TryCreateHttpUri(Uri pageUri, string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(pageUri, value, out var candidate) ||
            candidate.Scheme is not ("http" or "https"))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static async Task WriteValidatedImageAsync(string destination, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            if (!await IsDecodableImageAsync(temporary, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("Downloaded visual is not a supported image.");
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task<bool> IsDecodableImageAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            _ = await BitmapDecoder.CreateAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            return false;
        }
    }

    private static async Task<byte[]> ReadStreamAsync(StorageItemThumbnail stream, int size, CancellationToken cancellationToken)
    {
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var loaded = await reader.LoadAsync((uint)size);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = new byte[loaded];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return memory.ToArray();
            }

            if (memory.Length + read > limit)
            {
                throw new InvalidDataException("Visual exceeds the allowed size.");
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string CreateTextPreview(string address, string description)
    {
        var source = string.IsNullOrWhiteSpace(address) ? description : address;
        var lines = source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var preview = string.Join(Environment.NewLine, lines.Take(3));
        return preview.Length <= 180 ? preview : preview[..177] + "…";
    }

    private static string TryGetExecutablePath(string command)
    {
        var trimmed = command.Trim();
        if (File.Exists(trimmed))
        {
            return trimmed;
        }

        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : string.Empty;
        }

        var separator = trimmed.IndexOf(' ');
        return separator > 0 ? trimmed[..separator] : trimmed;
    }

    private static string ResolveShellSourcePath(ResourceType type, string address)
    {
        var path = type == ResourceType.Command ? TryGetExecutablePath(address) : address.Trim();
        if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsShortcutResolver.ResolveTargetPath(path) ?? path;
        }

        return path;
    }

    private static string CreateFingerprint(ResourceType type, string address, string description)
    {
        var source = $"{type}|{address.Trim()}|{(type == ResourceType.TextSnippet ? description : string.Empty)}";
        if (type is ResourceType.Application or ResourceType.File or ResourceType.Folder or ResourceType.Command)
        {
            source += $"|{ShellVisualCacheVersion}";
        }

        if (type is ResourceType.Application or ResourceType.File or ResourceType.Folder or ResourceType.Command or ResourceType.ImageSnippet)
        {
            try
            {
                var localPath = type == ResourceType.ImageSnippet ? address : ResolveShellSourcePath(type, address);
                if (File.Exists(localPath))
                {
                    var info = new FileInfo(localPath);
                    source += $"|{localPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                }
                else if (Directory.Exists(localPath))
                {
                    source += $"|{localPath}|{new DirectoryInfo(localPath).LastWriteTimeUtc.Ticks}";
                }
            }
            catch (IOException)
            {
            }
        }

        return HashText(source);
    }

    private ResourceVisualResolution Resolution(
        string absolutePath,
        ResourceVisualKind kind,
        ResourceVisualOrigin origin,
        string fingerprint,
        string sourceLabel) =>
        new(
            new ResourceVisualDescriptor(
                ResourceVisualMode.Auto,
                kind,
                ToRelativePath(absolutePath),
                origin,
                fingerprint),
            absolutePath,
            null,
            sourceLabel);

    private static ResourceVisualResolution Fallback(ResourceVisualMode mode, string fingerprint = "") =>
        new(
            new ResourceVisualDescriptor(mode, ResourceVisualKind.TypeFallback, null, ResourceVisualOrigin.Fallback, fingerprint),
            null,
            null,
            "Значок типа");

    private string CachePath(string fingerprint, string extension) =>
        Path.Combine(GetVisualRoot(), "cache", $"{fingerprint}{extension}");

    private string GetVisualRoot() => Path.Combine(_pathProvider.GetLocalDataRoot(), "Data", "Visuals");

    private string ToRelativePath(string absolutePath) =>
        Path.GetRelativePath(_pathProvider.GetLocalDataRoot(), absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private static async Task CopyIfMissingAsync(string source, string destination, CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string SourceLabel(ResourceVisualOrigin origin) => origin switch
    {
        ResourceVisualOrigin.UserProvided => "Пользовательское изображение",
        ResourceVisualOrigin.Payload => "Изображение ресурса",
        ResourceVisualOrigin.WindowsShell => "Значок Windows",
        ResourceVisualOrigin.RemoteMetadata => "Изображение сайта",
        ResourceVisualOrigin.Generated => "Предпросмотр",
        _ => "Значок типа",
    };
}
