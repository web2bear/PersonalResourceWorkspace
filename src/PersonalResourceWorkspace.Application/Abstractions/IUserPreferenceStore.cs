namespace PersonalResourceWorkspace.Application.Abstractions;

public interface IUserPreferenceStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}
