using PersonalResourceWorkspace.Domain.Resources;

namespace PersonalResourceWorkspace.Application.Models;

public sealed record ResourceVisualRequest(
    Guid ResourceId,
    ResourceType Type,
    string Address,
    string Description,
    ResourceVisualDescriptor Descriptor);

public sealed record ResourceVisualResolution(
    ResourceVisualDescriptor Descriptor,
    string? AbsoluteAssetPath,
    string? PreviewText,
    string SourceLabel);
