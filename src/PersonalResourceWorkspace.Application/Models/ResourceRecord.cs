namespace PersonalResourceWorkspace.Application.Models;

public sealed record ResourceRecord(
    Guid Id,
    string Title,
    string Description,
    string Kind,
    string Collection,
    string Modified,
    string Glyph,
    bool IsFavorite,
    string Address,
    ResourceVisualDescriptor Visual);
