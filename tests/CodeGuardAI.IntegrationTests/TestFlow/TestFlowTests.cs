using System.Net;
using System.Net.Http.Json;
using CodeGuardAI.Api.Contracts.Reviews;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Application.Workflows;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodeGuardAI.IntegrationTests.TestFlow;

public sealed class TestFlowTests : IDisposable
{
    private static readonly Guid ReviewId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FindingId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private readonly string _repositoryRoot = Path.Combine(
        Path.GetTempPath(),
        "CodeGuardAI.TestFlow",
        Guid.NewGuid().ToString("N"));

    public TestFlowTests()
    {
        Directory.CreateDirectory(_repositoryRoot);
        File.WriteAllText(Path.Combine(_repositoryRoot, "Source.cs"), "public class Source {}");
    }

    [Fact]
    public async Task Suggestions_are_persisted_without_running_tests_and_run_requires_explicit_action()
    {
        var store = CreateStore();
        var provider = new FakeTestProvider();
        var audit = new RecordingExecutionWriter();
        var runner = new FakeTestRunner(audit);
        await using var factory = CreateFactory(store, provider, runner, audit);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/tests",
            new CreateTestSuggestionsRequest
            {
                FindingIds = [FindingId],
                Model = "fake-test-model",
                TimeoutSeconds = 10,
                MaxSuggestions = 10
            });
        var suggestions = await created.Content.ReadFromJsonAsync<TestSuggestionBatchResponse>();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(suggestions);
        Assert.Single(suggestions.Tests);
        Assert.Single(store.TestCases);
        Assert.NotNull(store.ModelRun);
        Assert.Equal(AIModelRunPurpose.TestGeneration, store.ModelRun.Purpose);
        Assert.Equal(AIModelRunStatus.Succeeded, store.ModelRun.Status);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(0, runner.CallCount);

        using var duplicate = await client.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/tests",
            new CreateTestSuggestionsRequest
            {
                FindingIds = [FindingId],
                Model = "fake-test-model"
            });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(0, runner.CallCount);

        using var executed = await client.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/test-runs",
            new RunTestsRequest
            {
                ProjectPath = "tests/Safe.Tests.csproj",
                TimeoutSeconds = 10
            });
        var run = await executed.Content.ReadFromJsonAsync<TestRunResponse>();

        Assert.Equal(HttpStatusCode.OK, executed.StatusCode);
        Assert.NotNull(run);
        Assert.Equal(4, run.PassedCount);
        Assert.Equal(1, runner.CallCount);
        var toolAudit = Assert.Single(audit.Records, record => record.ToolName == ToolNames.TestRunner);
        Assert.Equal(ReviewId, toolAudit.ReviewRunId);
        Assert.Equal(ToolExecutionOutcome.Succeeded, toolAudit.Outcome);
    }

    private InMemoryTestWorkflowStore CreateStore()
    {
        var startedAt = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var review = ReviewRun.Start(
            ReviewId,
            ProjectId,
            "fake-review-model",
            "review-v1",
            startedAt);
        review.TryComplete(startedAt.AddSeconds(1));
        var finding = Finding.Create(
            FindingId,
            ReviewId,
            FindingSeverity.Medium,
            FindingCategory.CodeQuality,
            "Source.cs",
            1,
            1,
            "Improve naming",
            "The name is generic.",
            "Use a specific name.",
            0.8m,
            startedAt.AddSeconds(1));
        return new InMemoryTestWorkflowStore(
            new TestGenerationSource(review, _repositoryRoot, [finding]));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        InMemoryTestWorkflowStore store,
        FakeTestProvider provider,
        FakeTestRunner runner,
        RecordingExecutionWriter audit)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder
                    .UseEnvironment(Environments.Development)
                    .UseCodeGuardTestOptions();
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ITestWorkflowStore>();
                    services.RemoveAll<ILLMProvider>();
                    services.RemoveAll<ITestRunnerTool>();
                    services.RemoveAll<IToolExecutionWriter>();
                    services.AddSingleton<ITestWorkflowStore>(store);
                    services.AddSingleton<ILLMProvider>(provider);
                    services.AddSingleton<ITestRunnerTool>(runner);
                    services.AddSingleton<IToolExecutionWriter>(audit);
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

    private sealed class FakeTestProvider : ILLMProvider
    {
        public string Name => "fake";
        public int CallCount { get; private set; }

        public Task<LLMProviderResult> GenerateReviewAsync(
            LLMRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fake only supports test generation.");

        public Task<LLMProviderResult> GenerateTestsAsync(
            LLMRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(LLMProviderResult.Success(new LLMResponse(
                Name,
                request.Model,
                request.PromptVersion,
                """
                {"tests":[{"type":"regression","name":"Preserve source behavior","target":"Source.cs","scenario":"Exercise the reviewed behavior.","reason":"Prevents regression.","suggestedTestCode":null}]}
                """,
                TimeSpan.FromMilliseconds(12))));
        }
    }

    private sealed class FakeTestRunner(RecordingExecutionWriter audit) : ITestRunnerTool
    {
        public int CallCount { get; private set; }

        public async Task<ToolResult<TestRunnerOutput>> ExecuteAsync(
            TestRunnerToolInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            await audit.RecordAsync(
                new ToolExecutionRecord(
                    Guid.NewGuid(),
                    input.Execution.ReviewRunId,
                    ToolNames.TestRunner,
                    ToolExecutionOutcome.Succeeded,
                    5,
                    input.ToRedactedAuditSummary(),
                    "success=true; exitCode=0; passed=4; failed=0; skipped=0; truncated=false",
                    null,
                    new DateTimeOffset(2026, 10, 1, 8, 0, 5, TimeSpan.Zero)),
                cancellationToken);
            return ToolResult<TestRunnerOutput>.Success(
                new TestRunnerOutput(0, 4, 0, 0, "Passed: 4"),
                TimeSpan.FromMilliseconds(5));
        }
    }

    private sealed class RecordingExecutionWriter : IToolExecutionWriter
    {
        public List<ToolExecutionRecord> Records { get; } = [];

        public Task RecordAsync(ToolExecutionRecord execution, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Records.Add(execution);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryTestWorkflowStore(TestGenerationSource source) : ITestWorkflowStore
    {
        public List<TestCase> TestCases { get; } = [];
        public AIModelRun? ModelRun { get; private set; }

        public Task<TestGenerationSource?> GetSourceAsync(
            Guid reviewId,
            CancellationToken cancellationToken) =>
            Task.FromResult<TestGenerationSource?>(reviewId == source.ReviewRun.Id ? source : null);

        public Task<bool> HasCompletedGenerationAsync(
            Guid reviewId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ModelRun?.Status == AIModelRunStatus.Succeeded);

        public Task<bool> TryCompleteGenerationAsync(
            IReadOnlyCollection<TestCase> testCases,
            AIModelRun modelRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TestCases.AddRange(testCases);
            ModelRun = modelRun;
            return Task.FromResult(true);
        }

        public Task SaveFailedModelRunAsync(
            AIModelRun modelRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ModelRun = modelRun;
            return Task.CompletedTask;
        }
    }
}
