using System.Net;
using System.Text.Json;
using CodeGuardAI.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodeGuardAI.IntegrationTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Get_health_returns_only_the_typed_public_status()
    {
        await using var factory = CreateFactory(Environments.Development);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var responseBody = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(responseBody);
        var payload = JsonSerializer.Deserialize<HealthResponse>(
            responseBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(["status"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload.Status);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_without_internal_data()
    {
        await using var factory = CreateFactory(Environments.Development);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/not-a-real-route");
        var responseBody = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(responseBody);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("connection", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenApi_is_available_in_development_and_contains_health_contract()
    {
        await using var factory = CreateFactory(Environments.Development);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        var responseBody = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(responseBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/health", out _));
    }

    [Fact]
    public async Task OpenApi_is_not_exposed_outside_development()
    {
        await using var factory = CreateFactory(Environments.Production);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder
                    .UseEnvironment(environment)
                    .UseCodeGuardTestOptions());
    }
}
