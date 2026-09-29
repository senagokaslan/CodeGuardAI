using CodeGuardAI.Application.Options;
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

        services.AddCodeGuardPersistence();
        return services;
    }
}
