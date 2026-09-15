using PersonalResourceWorkspace.Domain.Resources;

namespace PersonalResourceWorkspace.Application.Resources;

public static class StoredResourceKinds
{
    public const string Application = "Приложение";
    public const string File = "Файл";
    public const string Folder = "Папка";
    public const string Web = "Web-ссылка";
    public const string LegacyWeb = "Ссылка";
    public const string Note = "Заметка";
    public const string Image = "Изображение";
    public const string Command = "Команда";
    public const string Collection = "Коллекция";

    public static readonly string[] EditorKinds = [Application, Web, File, Folder, Note, Image, Command];
}

public static class ResourceTypeMapper
{
    public static ResourceType FromStoredKind(string? kind)
    {
        if (string.Equals(kind, StoredResourceKinds.Web, StringComparison.Ordinal) ||
            string.Equals(kind, StoredResourceKinds.LegacyWeb, StringComparison.Ordinal))
        {
            return ResourceType.Web;
        }

        if (string.Equals(kind, StoredResourceKinds.Folder, StringComparison.Ordinal))
        {
            return ResourceType.Folder;
        }

        if (string.Equals(kind, StoredResourceKinds.Application, StringComparison.Ordinal))
        {
            return ResourceType.Application;
        }

        if (string.Equals(kind, StoredResourceKinds.Note, StringComparison.Ordinal))
        {
            return ResourceType.TextSnippet;
        }

        if (string.Equals(kind, StoredResourceKinds.Image, StringComparison.Ordinal))
        {
            return ResourceType.ImageSnippet;
        }

        if (string.Equals(kind, StoredResourceKinds.Command, StringComparison.Ordinal))
        {
            return ResourceType.Command;
        }

        if (string.Equals(kind, StoredResourceKinds.Collection, StringComparison.Ordinal))
        {
            return ResourceType.Collection;
        }

        return ResourceType.File;
    }

    public static string ToStoredKind(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Application:
                return StoredResourceKinds.Application;
            case ResourceType.Web:
                return StoredResourceKinds.Web;
            case ResourceType.File:
                return StoredResourceKinds.File;
            case ResourceType.Folder:
                return StoredResourceKinds.Folder;
            case ResourceType.TextSnippet:
                return StoredResourceKinds.Note;
            case ResourceType.ImageSnippet:
                return StoredResourceKinds.Image;
            case ResourceType.Command:
                return StoredResourceKinds.Command;
            case ResourceType.Collection:
                return StoredResourceKinds.Collection;
            default:
                ResourceType unreachable = type;
                throw new InvalidOperationException($"Unknown resource type: {unreachable}");
        }
    }

    public static string NormalizeStoredKind(string? kind) => ToStoredKind(FromStoredKind(kind));

    public static string GlyphFor(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Application:
                return "\uE8A5";
            case ResourceType.Web:
                return "\uE71B";
            case ResourceType.File:
                return "\uE8A5";
            case ResourceType.Folder:
                return "\uE8B7";
            case ResourceType.TextSnippet:
                return "\uE70F";
            case ResourceType.ImageSnippet:
                return "\uEB9F";
            case ResourceType.Command:
                return "\uE756";
            case ResourceType.Collection:
                return "\uE8B7";
            default:
                ResourceType unreachable = type;
                throw new InvalidOperationException($"Unknown resource type: {unreachable}");
        }
    }

    public static string GlyphForStoredKind(string? kind) => GlyphFor(FromStoredKind(kind));
}
