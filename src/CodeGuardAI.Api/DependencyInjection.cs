using CodeGuardAI.Application.Options;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Infrastructure;
using CodeGuardAI.Infrastructure.LLM;

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
                options => !string.IsNullOrWhiteSpace(options.ApiKey) &&
                           Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var baseAddress) &&
                           baseAddress.Scheme == Uri.UriSchemeHttps &&
                           options.MaxRetries is >= 0 and <= 2 &&
                           options.RetryBaseDelayMilliseconds >= 0 &&
                           options.MaxRetryJitterMilliseconds >= 0 &&
                           options.MaxRetryDelayMilliseconds > 0,
                "Gemini configuration is invalid; Gemini:ApiKey is required and retry limits must be bounded.")
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
