using PersonalResourceWorkspace.Application.Models;

namespace PersonalResourceWorkspace.Application.Abstractions;

public interface IResourceRepository
{
    Task<IReadOnlyList<ResourceRecord>> GetAllAsync(CancellationToken cancellationToken);
    Task SaveAsync(ResourceRecord resource, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteByCollectionAsync(string collectionName, CancellationToken cancellationToken);
}
