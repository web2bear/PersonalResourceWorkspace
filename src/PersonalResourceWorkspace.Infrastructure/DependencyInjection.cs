using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Infrastructure.Persistence;
using PersonalResourceWorkspace.Infrastructure.Storage;

namespace PersonalResourceWorkspace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<PortableDataDirectory>();
        services.AddSingleton<IPortableDataDirectory>(provider => provider.GetRequiredService<PortableDataDirectory>());
        services.TryAddSingleton<IAppDataPathProvider>(provider => provider.GetRequiredService<PortableDataDirectory>());
        services.AddSingleton<ISqliteConnectionFactory>(serviceProvider =>
        {
            var pathProvider = serviceProvider.GetRequiredService<IAppDataPathProvider>();
            var databasePath = Path.Combine(pathProvider.GetLocalDataRoot(), "Data", "workspace.db");
            return new SqliteConnectionFactory(databasePath);
        });
        services.AddSingleton<IApplicationInitializer, SqliteMigrationRunner>();
        services.AddSingleton<IUserPreferenceStore, SqliteUserPreferenceStore>();
        services.AddSingleton<ICollectionViewPreferenceStore, SqliteCollectionViewPreferenceStore>();
        services.AddSingleton<IResourceRepository, SqliteResourceRepository>();
        services.AddSingleton<ICollectionRepository, SqliteCollectionRepository>();
        return services;
    }
}
