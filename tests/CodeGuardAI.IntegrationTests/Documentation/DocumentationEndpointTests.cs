using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeGuardAI.Api.Contracts.Reviews;
using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Workflows;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodeGuardAI.IntegrationTests.Documentation;

public sealed class DocumentationEndpointTests
{
    private static readonly Guid ReviewId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReportId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ModelRunId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Post_returns_201_markdown_and_canonical_get_location()
    {
        var orchestrator = new StubDocsOrchestrator
        {
            CreateResult = Result.Success(CreateReadModel())
        };
        await using var factory = CreateFactory(orchestrator);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/documentation",
            ValidRequest());
        var body = await response.Content.ReadFromJsonAsync<DocumentationResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            $"/api/reviews/{ReviewId}/documentation",
            response.Headers.Location?.AbsolutePath);
        Assert.NotNull(body);
        Assert.Equal(ReportId, body.Id);
        Assert.Equal("# Technical report", body.MarkdownContent);
        Assert.Equal("gemini-docs-model", orchestrator.LastCreateCommand?.Policy.Model);
        Assert.Equal(TimeSpan.FromSeconds(90), orchestrator.LastCreateCommand?.Policy.Timeout);
        Assert.True(orchestrator.CreateCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task Post_invalid_request_returns_400_without_calling_orchestrator()
    {
        var orchestrator = new StubDocsOrchestrator();
        await using var factory = CreateFactory(orchestrator);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/documentation",
            new CreateDocumentationRequest { Model = "", TimeoutSeconds = 0 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation.failed", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, orchestrator.CreateCallCount);
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound, "documentation.review_not_found")]
    [InlineData("incomplete", HttpStatusCode.Conflict, "documentation.review_not_completed")]
    [InlineData("duplicate", HttpStatusCode.Conflict, "documentation.already_generated")]
    public async Task Post_maps_expected_workflow_errors(
        string scenario,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var error = scenario switch
        {
            "missing" => DocumentationWorkflowErrors.ReviewNotFound,
            "incomplete" => DocumentationWorkflowErrors.ReviewNotCompleted,
            "duplicate" => DocumentationWorkflowErrors.AlreadyGenerated,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var orchestrator = new StubDocsOrchestrator
        {
            CreateResult = Result.Failure<DocumentationReadModel>(error)
        };
        await using var factory = CreateFactory(orchestrator);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/documentation",
            ValidRequest());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_generation_failure_returns_safe_problem_details()
    {
        var orchestrator = new StubDocsOrchestrator
        {
            CreateResult = Result.Failure<DocumentationReadModel>(Error.Failure(
                "documentation.generation_failed",
                "SENSITIVE_PROVIDER_DETAIL"))
        };
        await using var factory = CreateFactory(orchestrator);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/documentation",
            ValidRequest());
        var responseBody = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(responseBody);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("documentation.generation_failed", json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("SENSITIVE_PROVIDER_DETAIL", responseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_returns_persisted_markdown()
    {
        var orchestrator = new StubDocsOrchestrator
        {
            GetResult = Result.Success(CreateReadModel())
        };
        await using var factory = CreateFactory(orchestrator);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/reviews/{ReviewId}/documentation");
        var body = await response.Content.ReadFromJsonAsync<DocumentationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("# Technical report", body.MarkdownContent);
        Assert.Equal(ReviewId, orchestrator.LastGetReviewId);
        Assert.True(orchestrator.GetCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task Get_missing_report_returns_404()
    {
        var orchestrator = new StubDocsOrchestrator
        {
            GetResult = Result.Failure<DocumentationReadModel>(
                DocumentationWorkflowErrors.ReportNotFound)
        };
        await using var factory = CreateFactory(orchestrator);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/reviews/{ReviewId}/documentation");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "documentation.report_not_found",
            json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_propagates_client_cancellation_to_orchestrator()
    {
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var orchestrator = new StubDocsOrchestrator
        {
            CreateHandler = async (_, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult();
                    throw;
                }

                throw new InvalidOperationException("Cancellation fixture unexpectedly continued.");
            }
        };
        await using var factory = CreateFactory(orchestrator);
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource();

        var operation = client.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/documentation",
            ValidRequest(),
            cancellation.Token);
        await WaitUntilAsync(() => orchestrator.CreateCallCount == 1);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Development_openapi_contains_get_and_post_documentation_operations()
    {
        await using var factory = CreateFactory(new StubDocsOrchestrator());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var route = json.RootElement
            .GetProperty("paths")
            .GetProperty("/api/reviews/{reviewId}/documentation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(route.TryGetProperty("post", out var post));
        Assert.True(route.TryGetProperty("get", out var get));
        Assert.True(post.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(post.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(post.GetProperty("responses").TryGetProperty("404", out _));
        Assert.True(post.GetProperty("responses").TryGetProperty("409", out _));
        Assert.True(post.GetProperty("responses").TryGetProperty("500", out _));
        Assert.True(get.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(get.GetProperty("responses").TryGetProperty("404", out _));
    }

    private static CreateDocumentationRequest ValidRequest() =>
        new() { Model = "gemini-docs-model", TimeoutSeconds = 90 };

    private static DocumentationReadModel CreateReadModel() =>
        new(
            ReportId,
            ReviewId,
            ModelRunId,
            "Technical report",
            "# Technical report",
            CreatedAtUtc);

    private static WebApplicationFactory<Program> CreateFactory(
        StubDocsOrchestrator orchestrator)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder
                    .UseEnvironment(Environments.Development)
                    .UseCodeGuardTestOptions();
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDocsOrchestrator>();
                    services.AddSingleton<IDocsOrchestrator>(orchestrator);
                });
            });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StubDocsOrchestrator : IDocsOrchestrator
    {
        public Result<DocumentationReadModel> CreateResult { get; init; } =
            Result.Failure<DocumentationReadModel>(DocumentationWorkflowErrors.ReviewNotFound);

        public Result<DocumentationReadModel> GetResult { get; init; } =
            Result.Failure<DocumentationReadModel>(DocumentationWorkflowErrors.ReportNotFound);

        public Func<CreateDocumentationCommand, CancellationToken, Task<Result<DocumentationReadModel>>>?
            CreateHandler
        { get; init; }

        public int CreateCallCount { get; private set; }

        public CreateDocumentationCommand? LastCreateCommand { get; private set; }

        public CancellationToken CreateCancellationToken { get; private set; }

        public Guid? LastGetReviewId { get; private set; }

        public CancellationToken GetCancellationToken { get; private set; }

        public Task<Result<DocumentationReadModel>> CreateAsync(
            CreateDocumentationCommand command,
            CancellationToken cancellationToken)
        {
            CreateCallCount++;
            LastCreateCommand = command;
            CreateCancellationToken = cancellationToken;
            return CreateHandler is null
                ? Task.FromResult(CreateResult)
                : CreateHandler(command, cancellationToken);
        }

        public Task<Result<DocumentationReadModel>> GetAsync(
            Guid reviewId,
            CancellationToken cancellationToken)
        {
            LastGetReviewId = reviewId;
            GetCancellationToken = cancellationToken;
            return Task.FromResult(GetResult);
        }
    }
}
