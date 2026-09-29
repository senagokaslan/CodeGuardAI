using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeGuardAI.Api.Contracts.Projects;
using CodeGuardAI.Api.Errors;
using CodeGuardAI.Application.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodeGuardAI.IntegrationTests;

public sealed class ContractValidationTests
{
    [Fact]
    public async Task Invalid_project_request_returns_deterministic_validation_problem()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var request = new CreateProjectRequest
        {
            Name = string.Empty,
            RepositoryPath = string.Empty
        };

        using var response = await client.PostAsJsonAsync("/_test/contracts/projects", request);
        var responseBody = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(responseBody);
        var root = json.RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("urn:codeguard:error:validation.failed", root.GetProperty("type").GetString());
        Assert.Equal("Request validation failed.", root.GetProperty("title").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("validation.failed", root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.True(root.GetProperty("errors").TryGetProperty("name", out _));
        Assert.True(root.GetProperty("errors").TryGetProperty("repositoryPath", out _));
    }

    [Theory]
    [InlineData("not-found", HttpStatusCode.NotFound, "project.not_found")]
    [InlineData("conflict", HttpStatusCode.Conflict, "project.conflict")]
    [InlineData("validation", HttpStatusCode.BadRequest, "project.invalid")]
    public async Task Result_error_maps_to_problem_details(
        string errorRoute,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/_test/contracts/errors/{errorRoute}");
        var responseBody = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(responseBody);
        var root = json.RootElement;

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder
                    .UseEnvironment(Environments.Development)
                    .UseCodeGuardTestOptions();
                builder.ConfigureServices(services =>
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(ProjectContractProbeController).Assembly));
            });
    }
}

[ApiController]
[Route("_test/contracts")]
public sealed class ProjectContractProbeController : ControllerBase
{
    [HttpPost("projects")]
    public ActionResult<ProjectResponse> Create(CreateProjectRequest request)
    {
        return Ok(new ProjectResponse(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            request.Name!,
            request.RepositoryPath!));
    }

    [HttpGet("errors/not-found")]
    public IActionResult NotFoundResult()
    {
        var result = Result.Failure(Error.NotFound("project.not_found", "Project was not found."));
        return ResultErrorMapper.ToActionResult(result.Error, HttpContext);
    }

    [HttpGet("errors/conflict")]
    public IActionResult ConflictResult()
    {
        var result = Result.Failure(Error.Conflict("project.conflict", "Project already exists."));
        return ResultErrorMapper.ToActionResult(result.Error, HttpContext);
    }

    [HttpGet("errors/validation")]
    public IActionResult ValidationResult()
    {
        var result = Result.Failure(Error.Validation("project.invalid", "Project is invalid."));
        return ResultErrorMapper.ToActionResult(result.Error, HttpContext);
    }
}
