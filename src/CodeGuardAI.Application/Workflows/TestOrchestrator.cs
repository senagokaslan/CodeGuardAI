using System.Diagnostics;
using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Context;
using CodeGuardAI.Application.Prompts;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;

namespace CodeGuardAI.Application.Workflows;

public interface ITestOrchestrator
{
    Task<Result<TestSuggestionBatch>> CreateSuggestionsAsync(
        CreateTestSuggestionsCommand command,
        CancellationToken cancellationToken);

    Task<Result<TestRunReadModel>> RunAsync(
        RunTestsCommand command,
        CancellationToken cancellationToken);
}

public interface ITestWorkflowStore
{
    Task<TestGenerationSource?> GetSourceAsync(Guid reviewId, CancellationToken cancellationToken);

    Task<bool> HasCompletedGenerationAsync(Guid reviewId, CancellationToken cancellationToken);

    Task CompleteGenerationAsync(
        IReadOnlyCollection<TestCase> testCases,
        AIModelRun modelRun,
        CancellationToken cancellationToken);

    Task SaveFailedModelRunAsync(AIModelRun modelRun, CancellationToken cancellationToken);
}

public sealed record TestGenerationSource(
    ReviewRun ReviewRun,
    string RepositoryRoot,
    IReadOnlyList<Finding> Findings);

public sealed record CreateTestSuggestionsCommand(
    Guid ReviewId,
    IReadOnlyCollection<Guid> FindingIds,
    TestAgentPolicy Policy);

public sealed record RunTestsCommand(
    Guid ReviewId,
    string RelativeProjectPath,
    TimeSpan Timeout);

public sealed record TestSuggestionReadModel(
    Guid Id,
    TestCaseType Type,
    string Name,
    string Target,
    string Scenario,
    string Reason,
    string? SuggestedTestCode);

public sealed record TestSuggestionBatch(
    Guid ReviewId,
    Guid ModelRunId,
    IReadOnlyList<TestSuggestionReadModel> Tests);

public sealed record TestRunReadModel(
    Guid ReviewId,
    int ExitCode,
    int PassedCount,
    int FailedCount,
    int SkippedCount,
    bool Truncated,
    string Output);

public sealed class TestOrchestrator(
    ITestWorkflowStore store,
    IRepositoryScanner scanner,
    IRepositoryContextBuilder contextBuilder,
    ITestAgent testAgent,
    ITestRunnerTool testRunner,
    TimeProvider timeProvider) : ITestOrchestrator
{
    public async Task<Result<TestSuggestionBatch>> CreateSuggestionsAsync(
        CreateTestSuggestionsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var source = command.ReviewId == Guid.Empty
            ? null
            : await store.GetSourceAsync(command.ReviewId, cancellationToken);
        if (source is null)
        {
            return Result.Failure<TestSuggestionBatch>(TestWorkflowErrors.ReviewNotFound);
        }

        if (source.ReviewRun.Status != ReviewStatus.Completed)
        {
            return Result.Failure<TestSuggestionBatch>(TestWorkflowErrors.ReviewNotCompleted);
        }

        if (await store.HasCompletedGenerationAsync(command.ReviewId, cancellationToken))
        {
            return Result.Failure<TestSuggestionBatch>(TestWorkflowErrors.AlreadyGenerated);
        }

        var requestedIds = command.FindingIds.ToHashSet();
        var selectedFindings = source.Findings
            .Where(finding => requestedIds.Contains(finding.Id))
            .ToArray();
        if (requestedIds.Count == 0 ||
            requestedIds.Contains(Guid.Empty) ||
            selectedFindings.Length != requestedIds.Count)
        {
            return Result.Failure<TestSuggestionBatch>(TestWorkflowErrors.InvalidSelection);
        }

        var scan = await scanner.ScanAsync(source.RepositoryRoot, cancellationToken);
        if (scan.IsFailure)
        {
            return Result.Failure<TestSuggestionBatch>(TestWorkflowErrors.ContextFailed);
        }

        var context = await contextBuilder.BuildAsync(
            source.RepositoryRoot,
            scan.Value,
            cancellationToken);
        if (context.IsFailure)
        {
            return Result.Failure<TestSuggestionBatch>(TestWorkflowErrors.ContextFailed);
        }

        var stopwatch = Stopwatch.StartNew();
        var agentResult = await testAgent.RunAsync(
            new TestAgentInput(source.ReviewRun, selectedFindings, context.Value, command.Policy),
            cancellationToken);
        if (agentResult.IsFailure)
        {
            var failedRun = CreateFailedModelRun(
                source.ReviewRun.Id,
                command.Policy,
                stopwatch.Elapsed,
                agentResult.Error.Code);
            await store.SaveFailedModelRunAsync(failedRun, cancellationToken);
            return Result.Failure<TestSuggestionBatch>(TestWorkflowErrors.GenerationFailed);
        }

        var modelRun = CreateSuccessfulModelRun(agentResult.Value);
        await store.CompleteGenerationAsync(
            agentResult.Value.TestCases,
            modelRun,
            cancellationToken);
        return Result.Success(new TestSuggestionBatch(
            source.ReviewRun.Id,
            modelRun.Id,
            agentResult.Value.TestCases.Select(ToReadModel).ToArray()));
    }

    public async Task<Result<TestRunReadModel>> RunAsync(
        RunTestsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var source = command.ReviewId == Guid.Empty
            ? null
            : await store.GetSourceAsync(command.ReviewId, cancellationToken);
        if (source is null)
        {
            return Result.Failure<TestRunReadModel>(TestWorkflowErrors.ReviewNotFound);
        }

        if (source.ReviewRun.Status != ReviewStatus.Completed)
        {
            return Result.Failure<TestRunReadModel>(TestWorkflowErrors.ReviewNotCompleted);
        }

        TestRunnerToolInput input;
        try
        {
            input = new TestRunnerToolInput(
                source.RepositoryRoot,
                command.RelativeProjectPath,
                TestRunnerKind.DotNet,
                new ToolExecutionOptions(command.Timeout, source.ReviewRun.Id));
        }
        catch (ArgumentException)
        {
            return Result.Failure<TestRunReadModel>(TestWorkflowErrors.InvalidTestTarget);
        }

        var run = await testRunner.ExecuteAsync(input, cancellationToken);
        if (run.IsFailure)
        {
            return Result.Failure<TestRunReadModel>(TestWorkflowErrors.TestRunFailed);
        }

        return Result.Success(new TestRunReadModel(
            source.ReviewRun.Id,
            run.Value.ExitCode,
            run.Value.PassedCount,
            run.Value.FailedCount,
            run.Value.SkippedCount,
            run.Truncated,
            run.Value.Output));
    }

    private AIModelRun CreateFailedModelRun(
        Guid reviewId,
        TestAgentPolicy policy,
        TimeSpan duration,
        string errorCode) =>
        AIModelRun.Create(
            Guid.NewGuid(),
            reviewId,
            AIModelRunPurpose.TestGeneration,
            testAgent.ProviderName,
            policy.Model,
            TestPromptRegistry.CurrentVersion,
            ToMilliseconds(duration),
            0,
            0,
            AIModelRunStatus.Failed,
            errorCode,
            timeProvider.GetUtcNow());

    private AIModelRun CreateSuccessfulModelRun(TestAgentResult result) =>
        AIModelRun.Create(
            Guid.NewGuid(),
            result.ReviewRunId,
            AIModelRunPurpose.TestGeneration,
            result.Provider,
            result.Model,
            result.PromptVersion,
            ToMilliseconds(result.Duration),
            result.InputCharacters,
            result.OutputCharacters,
            AIModelRunStatus.Succeeded,
            null,
            timeProvider.GetUtcNow());

    private static TestSuggestionReadModel ToReadModel(TestCase testCase) =>
        new(
            testCase.Id,
            testCase.Type,
            testCase.Name,
            testCase.Target,
            testCase.Scenario,
            testCase.Reason,
            testCase.SuggestedTestCode);

    private static int ToMilliseconds(TimeSpan duration) =>
        (int)Math.Min(int.MaxValue, Math.Max(0, duration.TotalMilliseconds));
}

public static class TestWorkflowErrors
{
    public static readonly Error ReviewNotFound = Error.NotFound(
        "tests.review_not_found",
        "The review was not found.");
    public static readonly Error ReviewNotCompleted = Error.Conflict(
        "tests.review_not_completed",
        "Test operations require a completed review.");
    public static readonly Error AlreadyGenerated = Error.Conflict(
        "tests.already_generated",
        "Test suggestions already exist for this review.");
    public static readonly Error InvalidSelection = Error.Validation(
        "tests.invalid_selection",
        "Selected findings must belong to the review.");
    public static readonly Error InvalidTestTarget = Error.Validation(
        "tests.target_invalid",
        "The test project target is invalid.");
    public static readonly Error ContextFailed = Error.Failure(
        "tests.context_failed",
        "Repository context could not be prepared.");
    public static readonly Error GenerationFailed = Error.Failure(
        "tests.generation_failed",
        "Test suggestions could not be generated.");
    public static readonly Error TestRunFailed = Error.Failure(
        "tests.run_failed",
        "The test run could not be completed.");
}
