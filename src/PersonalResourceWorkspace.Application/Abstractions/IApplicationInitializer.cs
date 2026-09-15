namespace PersonalResourceWorkspace.Application.Abstractions;

/// <summary>Runs non-UI startup work without blocking the UI thread.</summary>
public interface IApplicationInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
