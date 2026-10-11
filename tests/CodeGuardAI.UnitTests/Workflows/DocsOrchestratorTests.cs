using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Documentation;
using CodeGuardAI.Application.Prompts;
using CodeGuardAI.Application.Workflows;
using CodeGuardAI.Domain.Documentation;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeGuardAI.UnitTests.Workflows;

public sealed class DocsOrchestratorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Missing_review_returns_typed_not_found_without_running_agent()
    {
        var store = new FakeDocsWorkflowStore { Source = null };
        var agent = new FakeDocsAgent(CreateSuccessfulAgentResult(CreateCompletedReview().Id));

        var result = await CreateOrchestrator(store, agent).CreateAsync(
            CreateCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("documentation.review_not_found", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, agent.CallCount);
    }

    [Fact]
    public async Task Incomplete_review_returns_conflict_without_running_agent()
    {
        var review = CreateReview(ReviewStatus.Running);
        var store = new FakeDocsWorkflowStore { Source = CreateSource(review) };
        var agent = new FakeDocsAgent(CreateSuccessfulAgentResult(review.Id));

        var result = await CreateOrchestrator(store, agent).CreateAsync(
            CreateCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("documentation.review_not_completed", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(0, agent.CallCount);
        Assert.Empty(store.FailedRuns);
    }

    [Fact]
    public async Task Provider_failure_persists_only_failed_documentation_model_run()
    {
        var review = CreateCompletedReview();
        var store = new FakeDocsWorkflowStore { Source = CreateSource(review) };
        var agent = new FakeDocsAgent(Result.Failure<DocsAgentResult>(Error.Failure(
            "docs_agent.provider_unavailable",
            "Provider unavailable.")));

        var result = await CreateOrchestrator(store, agent).CreateAsync(
            CreateCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("documentation.generation_failed", result.Error.Code);
        var failedRun = Assert.Single(store.FailedRuns);
        Assert.Equal(AIModelRunPurpose.Documentation, failedRun.Purpose);
        Assert.Equal(AIModelRunStatus.Failed, failedRun.Status);
        Assert.Equal("docs_agent.provider_unavailable", failedRun.ErrorType);
        Assert.Equal(0, store.CompletionAttempts);
        Assert.Null(store.CompletedReport);
    }

    [Theory]
    [InlineData("docs_agent.invalid_response")]
    [InlineData("documentation.unknown_finding")]
    [InlineData("documentation.duplicate_test_case")]
    public async Task Parser_or_grounding_failure_persists_no_report(string errorCode)
    {
        var review = CreateCompletedReview();
        var store = new FakeDocsWorkflowStore { Source = CreateSource(review) };
        var agent = new FakeDocsAgent(Result.Failure<DocsAgentResult>(Error.Validation(
            errorCode,
            "Invalid model response.")));

        var result = await CreateOrchestrator(store, agent).CreateAsync(
            CreateCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("documentation.generation_failed", result.Error.Code);
        Assert.Equal(errorCode, Assert.Single(store.FailedRuns).ErrorType);
        Assert.Null(store.CompletedReport);
        Assert.Equal(0, store.CompletionAttempts);
    }

    [Fact]
    public async Task Cancellation_is_propagated_and_a_failed_model_run_is_audited()
    {
        var review = CreateCompletedReview();
        var store = new FakeDocsWorkflowStore { Source = CreateSource(review) };
        var agent = new FakeDocsAgent(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation fixture unexpectedly continued.");
        });
        using var cancellation = new CancellationTokenSource();

        var operation = CreateOrchestrator(store, agent).CreateAsync(
            CreateCommand(review.Id),
            cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        var failedRun = Assert.Single(store.FailedRuns);
        Assert.Equal("Cancelled", failedRun.ErrorType);
        Assert.False(store.FailedRunCancellationToken.IsCancellationRequested);
        Assert.Null(store.CompletedReport);
    }

    [Fact]
    public async Task Existing_report_returns_idempotency_conflict_before_agent_call()
    {
        var review = CreateCompletedReview();
        var store = new FakeDocsWorkflowStore
        {
            Source = CreateSource(review),
            HasCompletedReport = true
        };
        var agent = new FakeDocsAgent(CreateSuccessfulAgentResult(review.Id));

        var result = await CreateOrchestrator(store, agent).CreateAsync(
            CreateCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("documentation.already_generated", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(0, agent.CallCount);
    }

    [Fact]
    public async Task Concurrent_completion_loser_returns_safe_conflict()
    {
        var review = CreateCompletedReview();
        var store = new FakeDocsWorkflowStore
        {
            Source = CreateSource(review),
            CompleteResult = false
        };
        var agent = new FakeDocsAgent(CreateSuccessfulAgentResult(review.Id));

        var result = await CreateOrchestrator(store, agent).CreateAsync(
            CreateCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("documentation.already_generated", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(1, store.CompletionAttempts);
        Assert.Empty(store.FailedRuns);
    }

    [Fact]
    public async Task Success_persists_report_and_successful_model_run_in_one_store_call()
    {
        var review = CreateCompletedReview();
        var source = CreateSource(review);
        var store = new FakeDocsWorkflowStore { Source = source };
        var agent = new FakeDocsAgent(CreateSuccessfulAgentResult(review.Id, source.Findings));

        var result = await CreateOrchestrator(store, agent).CreateAsync(
            CreateCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.CompletionAttempts);
        Assert.NotNull(store.CompletedReport);
        Assert.NotNull(store.CompletedModelRun);
        Assert.Equal(store.CompletedModelRun.Id, store.CompletedReport.ModelRunId);
        Assert.Equal(AIModelRunStatus.Succeeded, store.CompletedModelRun.Status);
        Assert.Equal(AIModelRunPurpose.Documentation, store.CompletedModelRun.Purpose);
        Assert.Contains("Severity: High", store.CompletedReport.MarkdownContent);
        Assert.Equal("Example project", agent.LastInput?.ProjectName);
    }

    [Fact]
    public async Task Logs_do_not_include_markdown_prompt_or_model_text()
    {
        const string sensitiveModelText = "SENSITIVEMODELTEXT";
        var review = CreateCompletedReview();
        var source = CreateSource(review);
        var store = new FakeDocsWorkflowStore { Source = source };
        var successful = CreateSuccessfulAgentResult(review.Id, source.Findings).Value;
        var agent = new FakeDocsAgent(Result.Success(successful with
        {
            Report = successful.Report with { ExecutiveSummary = sensitiveModelText }
        }));
        var logger = new CapturingLogger<DocsOrchestrator>();

        var result = await CreateOrchestrator(store, agent, logger).CreateAsync(
            CreateCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(sensitiveModelText, result.Value.MarkdownContent);
        Assert.All(logger.Messages, message =>
            Assert.DoesNotContain(sensitiveModelText, message, StringComparison.Ordinal));
        Assert.All(logger.Messages, message =>
            Assert.DoesNotContain("docs-system", message, StringComparison.OrdinalIgnoreCase));
    }

    private static DocsOrchestrator CreateOrchestrator(
        IDocsWorkflowStore store,
        IDocsAgent agent,
        ILogger<DocsOrchestrator>? logger = null) =>
        new(
            store,
            agent,
            new MarkdownReportRenderer(),
            new FixedTimeProvider(Now),
            logger ?? NullLogger<DocsOrchestrator>.Instance);

    private static CreateDocumentationCommand CreateCommand(Guid reviewId) =>
        new(reviewId, new DocsAgentPolicy("docs-model", TimeSpan.FromSeconds(10)));

    private static DocsGenerationSource CreateSource(ReviewRun review)
    {
        var finding = Finding.Create(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            review.Id,
            FindingSeverity.High,
            FindingCategory.Security,
            "src/Service.cs",
            10,
            12,
            "Unsafe input",
            "Input is not validated",
            "Validate before use",
            0.9m,
            Now.AddMinutes(-2));
        return new DocsGenerationSource(review, "Example project", [finding], []);
    }

    private static Result<DocsAgentResult> CreateSuccessfulAgentResult(
        Guid reviewId,
        IReadOnlyList<Finding>? findings = null)
    {
        findings ??= [];
        var groundedFindings = findings
            .Select(finding => new GroundedDocumentationFinding(finding, "Impact"))
            .ToArray();
        return Result.Success(new DocsAgentResult(
            reviewId,
            "docs-model",
            DocsPromptRegistry.CurrentVersion,
            "fake",
            TimeSpan.FromMilliseconds(5),
            100,
            50,
            new GroundedDocumentationReport(
                "Technical report",
                "Summary",
                groundedFindings,
                [],
                "Conclusion"),
            groundedFindings.Length,
            0));
    }

    private static ReviewRun CreateCompletedReview() => CreateReview(ReviewStatus.Completed);

    private static ReviewRun CreateReview(ReviewStatus status)
    {
        var started = Now.AddMinutes(-5);
        var review = ReviewRun.Create(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "review-model",
            "review-v1",
            started);
        if (status != ReviewStatus.Pending)
        {
            review.TryStart();
        }

        if (status == ReviewStatus.Completed)
        {
            review.TryComplete(started.AddMinutes(1));
        }
        else if (status == ReviewStatus.Failed)
        {
            review.TryFail("ReviewFailed", started.AddMinutes(1));
        }

        return review;
    }

    private sealed class FakeDocsAgent : IDocsAgent
    {
        private readonly Func<DocsAgentInput, CancellationToken, Task<Result<DocsAgentResult>>> _run;

        public FakeDocsAgent(Result<DocsAgentResult> result)
            : this((_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(result);
            })
        {
        }

        public FakeDocsAgent(
            Func<DocsAgentInput, CancellationToken, Task<Result<DocsAgentResult>>> run)
        {
            _run = run;
        }

        public string ProviderName => "fake";
        public int CallCount { get; private set; }
        public DocsAgentInput? LastInput { get; private set; }

        public Task<Result<DocsAgentResult>> RunAsync(
            DocsAgentInput input,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastInput = input;
            return _run(input, cancellationToken);
        }
    }

    private sealed class FakeDocsWorkflowStore : IDocsWorkflowStore
    {
        public DocsGenerationSource? Source { get; init; }
        public bool HasCompletedReport { get; init; }
        public bool CompleteResult { get; init; } = true;
        public List<AIModelRun> FailedRuns { get; } = [];
        public int CompletionAttempts { get; private set; }
        public DocumentationReport? CompletedReport { get; private set; }
        public AIModelRun? CompletedModelRun { get; private set; }
        public CancellationToken FailedRunCancellationToken { get; private set; }

        public Task<DocsGenerationSource?> GetSourceAsync(
            Guid reviewId,
            CancellationToken cancellationToken) => Task.FromResult(Source);

        public Task<bool> HasCompletedReportAsync(
            Guid reviewId,
            CancellationToken cancellationToken) => Task.FromResult(HasCompletedReport);

        public Task<bool> TryCompleteAsync(
            DocumentationReport report,
            AIModelRun modelRun,
            CancellationToken cancellationToken)
        {
            CompletionAttempts++;
            if (CompleteResult)
            {
                CompletedReport = report;
                CompletedModelRun = modelRun;
            }

            return Task.FromResult(CompleteResult);
        }

        public Task SaveFailedModelRunAsync(
            AIModelRun modelRun,
            CancellationToken cancellationToken)
        {
            FailedRuns.Add(modelRun);
            FailedRunCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<DocumentationReadModel?> GetAsync(
            Guid reviewId,
            CancellationToken cancellationToken) => Task.FromResult<DocumentationReadModel?>(null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
