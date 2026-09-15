using Microsoft.Extensions.DependencyInjection;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Infrastructure.Persistence;

namespace PersonalResourceWorkspace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISqliteConnectionFactory>(serviceProvider =>
        {
            var pathProvider = serviceProvider.GetRequiredService<IAppDataPathProvider>();
            var databasePath = Path.Combine(pathProvider.GetLocalDataRoot(), "Data", "workspace.db");
            return new SqliteConnectionFactory(databasePath);
        });
        services.AddSingleton<IApplicationInitializer, SqliteMigrationRunner>();
        services.AddSingleton<IResourceRepository, SqliteResourceRepository>();
        services.AddSingleton<ICollectionRepository, SqliteCollectionRepository>();
        return services;
    }
}
