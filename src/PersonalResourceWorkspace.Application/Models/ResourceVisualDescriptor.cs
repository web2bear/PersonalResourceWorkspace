namespace PersonalResourceWorkspace.Application.Models;

public enum ResourceVisualMode
{
    Auto,
    UserAsset,
    TypeFallback,
}

public enum ResourceVisualKind
{
    Image,
    Icon,
    GeneratedPreview,
    TypeFallback,
}

public enum ResourceVisualOrigin
{
    UserProvided,
    Payload,
    WindowsShell,
    RemoteMetadata,
    Generated,
    Fallback,
}

public sealed record ResourceVisualDescriptor(
    ResourceVisualMode Mode,
    ResourceVisualKind Kind,
    string? AssetPath,
    ResourceVisualOrigin Origin,
    string SourceFingerprint)
{
    public static ResourceVisualDescriptor Auto { get; } = new(
        ResourceVisualMode.Auto,
        ResourceVisualKind.TypeFallback,
        null,
        ResourceVisualOrigin.Fallback,
        string.Empty);

    public static ResourceVisualDescriptor Fallback { get; } = new(
        ResourceVisualMode.TypeFallback,
        ResourceVisualKind.TypeFallback,
        null,
        ResourceVisualOrigin.Fallback,
        string.Empty);
}
