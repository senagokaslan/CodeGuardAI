using CodeGuardAI.Application.Options;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Infrastructure.Persistence;
using CodeGuardAI.Infrastructure.Persistence.Queries;
using CodeGuardAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodeGuardAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCodeGuardPersistence(this IServiceCollection services)
    {
        services.AddDbContext<CodeGuardDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider
                .GetRequiredService<IOptions<DatabaseOptions>>()
                .Value;
            var connectionString = databaseOptions.ConnectionString;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Validated configuration key Database:ConnectionString is unavailable.");
            }

            options.UseNpgsql(connectionString);
        });
        services.AddScoped<IProjectWriter, EfProjectWriter>();
        services.AddScoped<IProjectQueries, EfProjectQueries>();
        services.AddScoped<IRepositoryScanner, RepositoryScanner>();

        return services;
    }
}
