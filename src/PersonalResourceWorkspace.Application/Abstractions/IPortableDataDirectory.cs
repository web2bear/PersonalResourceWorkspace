namespace PersonalResourceWorkspace.Application.Abstractions;

public interface IPortableDataDirectory : IAppDataPathProvider
{
    string Source { get; }
    bool IsOverridden { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task RelocateAsync(string destination, CancellationToken cancellationToken);
}
