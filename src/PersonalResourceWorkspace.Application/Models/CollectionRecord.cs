namespace PersonalResourceWorkspace.Application.Models;

public sealed record CollectionRecord(
    Guid Id,
    string Name,
    string Glyph,
    string Color,
    int SortOrder,
    bool IsDefault,
    bool IsTrash = false,
    string? VisualAssetPath = null);
