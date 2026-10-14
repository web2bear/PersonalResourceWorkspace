namespace PersonalResourceWorkspace.Application.Models;

public sealed record TagRecord(Guid Id, string Name, bool IsSystem);

public sealed record SavedSearchRecord(
    Guid Id,
    string Name,
    Guid? ParentId,
    string Glyph,
    string Color,
    int SortOrder,
    bool IsSystem,
    IReadOnlyList<Guid> RequiredTagIds);

public sealed record TaggedResourceRecord(
    Guid Id,
    string Title,
    string Description,
    string Kind,
    string Modified,
    string Glyph,
    string Address,
    ResourceVisualDescriptor Visual,
    IReadOnlyList<Guid> TagIds);
