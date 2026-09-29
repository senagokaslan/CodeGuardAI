using CodeGuardAI.Application.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodeGuardAI.IntegrationTests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Valid_configuration_binds_strongly_typed_options()
    {
        using var factory = CreateFactory();

        var database = factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var gemini = factory.Services.GetRequiredService<IOptions<GeminiOptions>>().Value;

        Assert.Equal(IntegrationTestConfiguration.FakeConnectionString, database.ConnectionString);
        Assert.Equal(IntegrationTestConfiguration.FakeGeminiApiKey, gemini.ApiKey);
    }

    [Fact]
    public void Missing_database_setting_fails_without_exposing_other_secret_values()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = string.Empty
        });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Database:ConnectionString", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            IntegrationTestConfiguration.FakeGeminiApiKey,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_gemini_setting_fails_without_exposing_other_secret_values()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "   "
        });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Gemini:ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            IntegrationTestConfiguration.FakeConnectionString,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Environment_variable_overrides_lower_precedence_configuration()
    {
        var prefix = $"CODEGUARD_TEST_{Guid.NewGuid():N}_";
        var variableName = $"{prefix}Gemini__ApiKey";
        const string environmentValue = "fake-environment-override";
        Environment.SetEnvironmentVariable(variableName, environmentValue);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Gemini:ApiKey"] = "fake-lower-precedence-value"
                })
                .AddEnvironmentVariables(prefix)
                .Build();

            Assert.Equal(environmentValue, configuration["Gemini:ApiKey"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder
                    .UseEnvironment(Environments.Development)
                    .UseCodeGuardTestOptions(overrides));
    }
}
