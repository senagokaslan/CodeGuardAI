using System.Net;
using System.Net.Http.Json;
using CodeGuardAI.Api.Contracts.Reviews;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Application.Workflows;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Projects;
using CodeGuardAI.Domain.Reviews;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodeGuardAI.IntegrationTests.ReviewFlow;

public sealed class ReviewFlowTests : IDisposable
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _repositoryRoot = Path.Combine(
        Path.GetTempPath(),
        "CodeGuardAI.ReviewFlow",
        Guid.NewGuid().ToString("N"));

    public ReviewFlowTests()
    {
        Directory.CreateDirectory(_repositoryRoot);
        File.WriteAllText(Path.Combine(_repositoryRoot, "Source.cs"), "public class Source {}");
    }

    [Fact]
    public async Task Create_scan_context_agent_persist_complete_flow_uses_one_fake_model_call()
    {
        var store = CreateStore();
        var provider = new QueuedFakeLLMProvider();
        provider.EnqueueSuccess("""
            {"findings":[{"severity":"medium","category":"codeQuality","filePath":"Source.cs","startLine":1,"endLine":1,"title":"Improve naming","reason":"The name is generic.","suggestion":"Use a specific name.","confidence":0.8}]}
            """);
        await using var factory = CreateFactory(store, provider);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync("/reviews", new CreateReviewRequest
        {
            ProjectId = ProjectId,
            Model = "fake-review-model",
            TimeoutSeconds = 10,
            MaxFindings = 20
        });
        var body = await created.Content.ReadFromJsonAsync<ReviewResponse>();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("Completed", body.Status);
        Assert.Single(body.Findings);
        Assert.Equal([ReviewStatus.Pending, ReviewStatus.Running, ReviewStatus.Completed], store.Transitions);
        Assert.Equal(1, store.AtomicCompletionCount);
        Assert.Equal(1, provider.CallCount);
        Assert.NotNull(store.ModelRun);
        Assert.Equal(AIModelRunStatus.Succeeded, store.ModelRun.Status);
        Assert.Single(store.Findings);

        var fetched = await client.GetFromJsonAsync<ReviewResponse>($"/reviews/{body.Id}");
        var history = await client.GetFromJsonAsync<ReviewHistoryResponse>($"/projects/{ProjectId}/reviews");
        Assert.Equal(body.Id, fetched?.Id);
        Assert.Equal(body.Id, Assert.Single(history!.Items).Id);
    }

    [Fact]
    public async Task Provider_failure_persists_failed_run_without_findings()
    {
        var store = CreateStore();
        var provider = new QueuedFakeLLMProvider();
        provider.EnqueueFailure(new LLMProviderError(
            LLMProviderErrorType.Unavailable,
            "fake.unavailable",
            "Unavailable."));
        await using var factory = CreateFactory(store, provider);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/reviews", new CreateReviewRequest
        {
            ProjectId = ProjectId,
            Model = "fake-review-model",
            TimeoutSeconds = 10
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal([ReviewStatus.Pending, ReviewStatus.Running, ReviewStatus.Failed], store.Transitions);
        Assert.Equal("ProviderUnavailable", store.ReviewRun?.ErrorCode);
        Assert.Empty(store.Findings);
        Assert.NotNull(store.ModelRun);
        Assert.Equal(AIModelRunStatus.Failed, store.ModelRun.Status);
        Assert.Equal(1, provider.CallCount);
    }

    private InMemoryReviewWorkflowStore CreateStore()
    {
        var project = Project.Create(
            ProjectId,
            "Review Flow",
            _repositoryRoot,
            Path.GetFullPath(_repositoryRoot),
            new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero));
        return new InMemoryReviewWorkflowStore(project);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        InMemoryReviewWorkflowStore store,
        QueuedFakeLLMProvider provider)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder
                    .UseEnvironment(Environments.Development)
                    .UseCodeGuardTestOptions();
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IReviewWorkflowStore>();
                    services.RemoveAll<ILLMProvider>();
                    services.RemoveAll<IToolExecutionWriter>();
                    services.AddSingleton<IReviewWorkflowStore>(store);
                    services.AddSingleton<ILLMProvider>(provider);
                    services.AddSingleton<IToolExecutionWriter, InMemoryToolExecutionWriter>();
                });
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_repositoryRoot))
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }
    }

    private sealed class QueuedFakeLLMProvider : ILLMProvider
    {
        private readonly Queue<LLMProviderResult> _results = [];

        public string Name => "fake";

        public int CallCount { get; private set; }

        public void EnqueueSuccess(string content)
        {
            _results.Enqueue(LLMProviderResult.Success(new LLMResponse(
                Name,
                "fake-review-model",
                "review-v1",
                content,
                TimeSpan.FromMilliseconds(10))));
        }

        public void EnqueueFailure(LLMProviderError error)
        {
            _results.Enqueue(LLMProviderResult.Failure(error));
        }

        public Task<LLMProviderResult> GenerateReviewAsync(
            LLMRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_results.Dequeue());
        }

        public Task<LLMProviderResult> GenerateTestsAsync(
            LLMRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fake only supports review generation.");
    }

    private sealed class InMemoryToolExecutionWriter : IToolExecutionWriter
    {
        public Task RecordAsync(
            ToolExecutionRecord execution,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryReviewWorkflowStore(Project project) : IReviewWorkflowStore
    {
        public List<ReviewStatus> Transitions { get; } = [];

        public int AtomicCompletionCount { get; private set; }

        public ReviewRun? ReviewRun { get; private set; }

        public List<Finding> Findings { get; } = [];

        public AIModelRun? ModelRun { get; private set; }

        public Task<Project?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<Project?>(projectId == project.Id ? project : null);

        public Task<bool> TryCreatePendingAsync(
            ReviewRun reviewRun,
            DateTimeOffset staleBeforeUtc,
            CancellationToken cancellationToken)
        {
            ReviewRun = reviewRun;
            Transitions.Add(reviewRun.Status);
            return Task.FromResult(true);
        }

        public Task SaveStateAsync(ReviewRun reviewRun, CancellationToken cancellationToken)
        {
            Transitions.Add(reviewRun.Status);
            return Task.CompletedTask;
        }

        public Task CompleteAsync(
            ReviewRun reviewRun,
            IReadOnlyCollection<Finding> findings,
            AIModelRun modelRun,
            CancellationToken cancellationToken)
        {
            AtomicCompletionCount++;
            Findings.AddRange(findings);
            ModelRun = modelRun;
            Transitions.Add(reviewRun.Status);
            return Task.CompletedTask;
        }

        public Task FailAsync(
            ReviewRun reviewRun,
            AIModelRun? modelRun,
            CancellationToken cancellationToken)
        {
            ModelRun = modelRun;
            Transitions.Add(reviewRun.Status);
            return Task.CompletedTask;
        }

        public Task<ReviewReadModel?> GetAsync(Guid reviewId, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                ReviewRun?.Id == reviewId ? ToReadModel(ReviewRun) : null);
        }

        public Task<IReadOnlyList<ReviewReadModel>> GetHistoryAsync(
            Guid projectId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<ReviewReadModel> result = ReviewRun?.ProjectId == projectId
                ? [ToReadModel(ReviewRun)]
                : [];
            return Task.FromResult(result);
        }

        private ReviewReadModel ToReadModel(ReviewRun reviewRun)
        {
            return new ReviewReadModel(
                reviewRun.Id,
                reviewRun.ProjectId,
                reviewRun.Status,
                reviewRun.ModelName,
                reviewRun.PromptVersion,
                reviewRun.StartedAtUtc,
                reviewRun.CompletedAtUtc,
                reviewRun.ErrorCode,
                Findings.Select(finding => new ReviewFindingReadModel(
                    finding.Id,
                    finding.FilePath,
                    finding.StartLine,
                    finding.EndLine,
                    finding.Severity,
                    finding.Category,
                    finding.Title,
                    finding.Reason,
                    finding.Suggestion,
                    finding.Confidence)).ToArray());
        }
    }
}
