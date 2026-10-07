using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodeGuardAI.IntegrationTests.DemoUi;

public sealed class DemoUiTests
{
    [Theory]
    [InlineData("/", "text/html")]
    [InlineData("/styles.css", "text/css")]
    [InlineData("/app.js", "text/javascript")]
    public async Task Static_demo_assets_are_served(string path, string mediaType)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Home_contains_accessible_labels_server_local_warning_and_explicit_test_action()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<label for=\"project-name\">", html, StringComparison.Ordinal);
        Assert.Contains("<label for=\"repository-path\">", html, StringComparison.Ordinal);
        Assert.Contains("Server-local path.", html, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"generate-tests\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"password\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Browser_code_renders_api_data_with_text_content_not_html_injection()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var script = await client.GetStringAsync("/app.js");

        Assert.Contains("body?.detail", script, StringComparison.Ordinal);
        Assert.Contains("body?.errors", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", script, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder
                .UseEnvironment(Environments.Development)
                .UseCodeGuardTestOptions());
    }
}
