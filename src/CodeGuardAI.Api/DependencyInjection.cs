using CodeGuardAI.Application.Options;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Infrastructure;

namespace CodeGuardAI.Api;

internal static class DependencyInjection
{
    public static IServiceCollection AddCodeGuardServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Configuration key Database:ConnectionString is required.")
            .ValidateOnStart();
        services
            .AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "Configuration key Gemini:ApiKey is required.")
            .ValidateOnStart();
        services
            .AddOptions<ScanOptions>()
            .Bind(configuration.GetSection(ScanOptions.SectionName))
            .Validate(
                options => options.MaxFileCount > 0 &&
                           options.MaxFileBytes > 0 &&
                           options.MaxTotalBytes > 0,
                "Scan limits must be positive.")
            .ValidateOnStart();

        services.AddCodeGuardPersistence();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IProjectService, ProjectService>();
        return services;
    }
}
