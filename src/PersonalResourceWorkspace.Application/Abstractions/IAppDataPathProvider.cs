namespace PersonalResourceWorkspace.Application.Abstractions;

/// <summary>Provides the application-owned local data root.</summary>
public interface IAppDataPathProvider
{
    string GetLocalDataRoot();
}
