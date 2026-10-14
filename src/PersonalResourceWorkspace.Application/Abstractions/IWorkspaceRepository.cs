using PersonalResourceWorkspace.Application.Models;

namespace PersonalResourceWorkspace.Application.Abstractions;

public interface IWorkspaceRepository
{
    Task<IReadOnlyList<TagRecord>> GetTagsAsync(CancellationToken cancellationToken);
    Task SaveTagAsync(TagRecord tag, CancellationToken cancellationToken);
    Task DeleteTagAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedSearchRecord>> GetSearchesAsync(CancellationToken cancellationToken);
    Task SaveSearchAsync(SavedSearchRecord search, CancellationToken cancellationToken);
    Task DeleteSearchTreeAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TaggedResourceRecord>> GetResourcesAsync(CancellationToken cancellationToken);
    Task SaveResourceAsync(TaggedResourceRecord resource, CancellationToken cancellationToken);
    Task DeleteResourceAsync(Guid id, CancellationToken cancellationToken);
}
