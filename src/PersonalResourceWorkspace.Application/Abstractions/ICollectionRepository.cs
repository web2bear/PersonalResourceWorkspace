using PersonalResourceWorkspace.Application.Models;

namespace PersonalResourceWorkspace.Application.Abstractions;

public interface ICollectionRepository
{
    Task<IReadOnlyList<CollectionRecord>> GetAllAsync(CancellationToken cancellationToken);

    Task SaveAsync(CollectionRecord collection, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, Guid reassignToId, CancellationToken cancellationToken);
}
