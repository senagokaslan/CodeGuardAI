using CodeGuardAI.Application.Options;
using CodeGuardAI.Infrastructure.Persistence;
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

        return services;
    }
}
