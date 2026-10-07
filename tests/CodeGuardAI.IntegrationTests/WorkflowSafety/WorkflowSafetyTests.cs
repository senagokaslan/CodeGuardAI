using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

namespace CodeGuardAI.IntegrationTests.WorkflowSafety;

public sealed class WorkflowSafetyTests : IDisposable
{
    private static readonly Guid ProjectId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private readonly string _repositoryRoot = Path.Combine(
        Path.GetTempPath(),
        "CodeGuardAI.WorkflowSafety",
        Guid.NewGuid().ToString("N"));

    public WorkflowSafetyTests()
    {
        Directory.CreateDirectory(_repositoryRoot);
        File.WriteAllText(Path.Combine(_repositoryRoot, "Source.cs"), "public class Source {}");
    }

    [Fact]
    public void Review_state_machine_rejects_a_step_beyond_its_fixed_budget()
    {
        var stateMachine = new ReviewWorkflowStateMachine(maxSteps: 2);

        stateMachine.MoveTo(ReviewWorkflowStage.PendingPersisted);
        stateMachine.MoveTo(ReviewWorkflowStage.Running);

        var exception = Assert.Throws<WorkflowStepLimitExceededException>(
            () => stateMachine.MoveTo(ReviewWorkflowStage.RepositoryScanned));

        Assert.Equal(2, exception.MaxSteps);
        Assert.Equal(nameof(ReviewWorkflowStage.RepositoryScanned), exception.Step);
        Assert.Equal(ReviewWorkflowStage.Running, stateMachine.Stage);
        Assert.Equal(2, stateMachine.StepsUsed);
    }

    [Fact]
    public async Task Concurrent_review_for_same_project_returns_conflict_and_first_run_persists_terminal_state()
    {
        var store = CreateStore(blockFirstAtRunning: true);
        var provider = new SuccessfulReviewProvider();
        await using var factory = CreateFactory(store, provider);
        using var client = factory.CreateClient();
        var request = CreateRequest();

        var firstRequest = client.PostAsJsonAsync("/reviews", request);
        await store.FirstRunIsRunning.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var duplicate = await client.PostAsJsonAsync("/reviews", request);
        using var duplicateBody = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("review.duplicate_active", duplicateBody.RootElement.GetProperty("code").GetString());

        store.ReleaseFirstRun();
        using var completed = await firstRequest;

        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);
        Assert.Equal(ReviewStatus.Completed, store.ReviewRun?.Status);
        Assert.NotNull(store.ReviewRun?.CompletedAtUtc);
        Assert.Equal([ReviewStatus.Pending, ReviewStatus.Running, ReviewStatus.Completed], store.Transitions);
        Assert.Equal(1, store.AtomicCompletionCount);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Cancellation_is_propagated_and_persisted_as_a_terminal_failed_run()
    {
        var store = CreateStore(blockFirstAtRunning: false);
        var provider = new CancellingReviewProvider();
        await using var factory = CreateFactory(store, provider);
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource();

        var request = client.PostAsJsonAsync("/reviews", CreateRequest(), cancellation.Token);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await store.TerminalStateSaved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ReviewStatus.Failed, store.ReviewRun?.Status);
        Assert.Equal(ReviewRun.CancelledErrorCode, store.ReviewRun?.ErrorCode);
        Assert.NotNull(store.ReviewRun?.CompletedAtUtc);
        Assert.Equal([ReviewStatus.Pending, ReviewStatus.Running, ReviewStatus.Failed], store.Transitions);
        Assert.Equal(1, provider.CallCount);
    }

    private CreateReviewRequest CreateRequest() => new()
    {
        ProjectId = ProjectId,
        Model = "fake-review-model",
        TimeoutSeconds = 10,
        MaxFindings = 20
    };

    private ConcurrentReviewWorkflowStore CreateStore(bool blockFirstAtRunning)
    {
        var project = Project.Create(
            ProjectId,
            "Workflow Safety",
            _repositoryRoot,
            Path.GetFullPath(_repositoryRoot),
            new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero));
        return new ConcurrentReviewWorkflowStore(project, blockFirstAtRunning);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        ConcurrentReviewWorkflowStore store,
        ILLMProvider provider)
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

    private sealed class SuccessfulReviewProvider : ILLMProvider
    {
        private int _callCount;

        public string Name => "fake";
        public int CallCount => _callCount;

        public Task<LLMProviderResult> GenerateReviewAsync(
            LLMRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(LLMProviderResult.Success(new LLMResponse(
                Name,
                "fake-review-model",
                "review-v1",
                """{"findings":[]}""",
                TimeSpan.FromMilliseconds(10))));
        }

        public Task<LLMProviderResult> GenerateTestsAsync(
            LLMRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CancellingReviewProvider : ILLMProvider
    {
        private int _callCount;

        public string Name => "fake";
        public int CallCount => _callCount;
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<LLMProviderResult> GenerateReviewAsync(
            LLMRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation token should stop the provider call.");
        }

        public Task<LLMProviderResult> GenerateTestsAsync(
            LLMRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InMemoryToolExecutionWriter : IToolExecutionWriter
    {
        public Task RecordAsync(ToolExecutionRecord execution, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentReviewWorkflowStore(
        Project project,
        bool blockFirstAtRunning) : IReviewWorkflowStore
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _releaseFirstRun =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstRunIsRunning { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TerminalStateSaved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ReviewStatus> Transitions { get; } = [];
        public ReviewRun? ReviewRun { get; private set; }
        public int AtomicCompletionCount { get; private set; }

        public void ReleaseFirstRun() => _releaseFirstRun.TrySetResult();

        public Task<Project?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<Project?>(projectId == project.Id ? project : null);

        public Task<bool> TryCreatePendingAsync(
            ReviewRun reviewRun,
            DateTimeOffset staleBeforeUtc,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (ReviewRun?.Status is ReviewStatus.Pending or ReviewStatus.Running)
                {
                    return Task.FromResult(false);
                }

                ReviewRun = reviewRun;
                Transitions.Add(reviewRun.Status);
                return Task.FromResult(true);
            }
        }

        public async Task SaveStateAsync(ReviewRun reviewRun, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Transitions.Add(reviewRun.Status);
            }

            if (reviewRun.Status == ReviewStatus.Running)
            {
                FirstRunIsRunning.TrySetResult();
                if (blockFirstAtRunning)
                {
                    await _releaseFirstRun.Task.WaitAsync(cancellationToken);
                }
            }
        }

        public Task CompleteAsync(
            ReviewRun reviewRun,
            IReadOnlyCollection<Finding> findings,
            AIModelRun modelRun,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                AtomicCompletionCount++;
                Transitions.Add(reviewRun.Status);
            }

            TerminalStateSaved.TrySetResult();
            return Task.CompletedTask;
        }

        public Task FailAsync(
            ReviewRun reviewRun,
            AIModelRun? modelRun,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Transitions.Add(reviewRun.Status);
            }

            TerminalStateSaved.TrySetResult();
            return Task.CompletedTask;
        }

        public Task<ReviewReadModel?> GetAsync(Guid reviewId, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(
                    ReviewRun?.Id == reviewId ? ToReadModel(ReviewRun) : null);
            }
        }

        public Task<IReadOnlyList<ReviewReadModel>> GetHistoryAsync(
            Guid projectId,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                IReadOnlyList<ReviewReadModel> result = ReviewRun?.ProjectId == projectId
                    ? [ToReadModel(ReviewRun)]
                    : [];
                return Task.FromResult(result);
            }
        }

        private static ReviewReadModel ToReadModel(ReviewRun reviewRun) => new(
            reviewRun.Id,
            reviewRun.ProjectId,
            reviewRun.Status,
            reviewRun.ModelName,
            reviewRun.PromptVersion,
            reviewRun.StartedAtUtc,
            reviewRun.CompletedAtUtc,
            reviewRun.ErrorCode,
            []);
    }
}
