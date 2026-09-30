using CodeGuardAI.Application.Options;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Application.Context;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Infrastructure.LLM;
using CodeGuardAI.Infrastructure.Persistence;
using CodeGuardAI.Infrastructure.Persistence.Queries;
using CodeGuardAI.Infrastructure.Repositories;
using CodeGuardAI.Infrastructure.Tools;
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
        services.AddSingleton<SafePathResolver>();
        services.AddScoped<IRepositoryScanner, RepositoryScanner>();
        services.AddSingleton(new RepositoryContextOptions());
        services.AddScoped<IFileReadTool, SafeFileReadTool>();
        services.AddScoped<IRepositoryContextBuilder, RepositoryContextBuilder>();
        services
            .AddHttpClient<GeminiHttpClient>((serviceProvider, client) =>
            {
                var geminiOptions = serviceProvider
                    .GetRequiredService<IOptions<GeminiOptions>>()
                    .Value;
                client.BaseAddress = new Uri(geminiOptions.BaseAddress, UriKind.Absolute);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .RedactLoggedHeaders(["x-goog-api-key"]);
        services.AddScoped<ILLMProvider, GeminiProvider>();

        return services;
    }
}
