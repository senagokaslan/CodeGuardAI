using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace CodeGuardAI.IntegrationTests;

internal static class IntegrationTestConfiguration
{
    internal const string FakeConnectionString = "fake-integration-database-connection";
    internal const string FakeGeminiApiKey = "fake-integration-gemini-key";

    internal static IWebHostBuilder UseCodeGuardTestOptions(
        this IWebHostBuilder builder,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Database:ConnectionString"] = FakeConnectionString,
            ["Gemini:ApiKey"] = FakeGeminiApiKey
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(values));

        return builder;
    }
}
