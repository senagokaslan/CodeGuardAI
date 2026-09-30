using System.Diagnostics;
using System.Text.Json;
using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Context;
using CodeGuardAI.Application.Prompts;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Projects;
using CodeGuardAI.Domain.Reviews;

namespace CodeGuardAI.Application.Workflows;

public interface IReviewOrchestrator
{
    Task<Result<ReviewReadModel>> CreateAsync(
        CreateReviewCommand command,
        CancellationToken cancellationToken);

    Task<Result<ReviewReadModel>> GetAsync(Guid reviewId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ReviewReadModel>>> GetHistoryAsync(
        Guid projectId,
        CancellationToken cancellationToken);
}

public interface IReviewWorkflowStore
{
    Task<Project?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken);

    Task CreatePendingAsync(ReviewRun reviewRun, CancellationToken cancellationToken);

    Task SaveStateAsync(ReviewRun reviewRun, CancellationToken cancellationToken);

    Task CompleteAsync(
        ReviewRun reviewRun,
        IReadOnlyCollection<Finding> findings,
        AIModelRun modelRun,
        CancellationToken cancellationToken);

    Task FailAsync(
        ReviewRun reviewRun,
        AIModelRun? modelRun,
        CancellationToken cancellationToken);

    Task<ReviewReadModel?> GetAsync(Guid reviewId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewReadModel>> GetHistoryAsync(
        Guid projectId,
        CancellationToken cancellationToken);
}

public sealed record CreateReviewCommand(
    Guid ProjectId,
    ReviewAgentPolicy Policy);

public sealed record ReviewFindingReadModel(
    Guid Id,
    string FilePath,
    int StartLine,
    int EndLine,
    FindingSeverity Severity,
    FindingCategory Category,
    string Title,
    string Reason,
    string Suggestion,
    decimal Confidence);

public sealed record ReviewReadModel(
    Guid Id,
    Guid ProjectId,
    ReviewStatus Status,
    string Model,
    string PromptVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorCode,
    IReadOnlyList<ReviewFindingReadModel> Findings);

public sealed class ReviewOrchestrator(
    IReviewWorkflowStore store,
    IRepositoryScanner scanner,
    IRepositoryContextBuilder contextBuilder,
    IReviewAgent reviewAgent,
    TimeProvider timeProvider) : IReviewOrchestrator
{
    public async Task<Result<ReviewReadModel>> CreateAsync(
        CreateReviewCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ProjectId == Guid.Empty)
        {
            return Result.Failure<ReviewReadModel>(ReviewWorkflowErrors.ProjectNotFound);
        }

        var project = await store.GetProjectAsync(command.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result.Failure<ReviewReadModel>(ReviewWorkflowErrors.ProjectNotFound);
        }

        var reviewRun = ReviewRun.Create(
            Guid.NewGuid(),
            project.Id,
            command.Policy.Model,
            ReviewPromptRegistry.CurrentVersion,
            timeProvider.GetUtcNow());
        await store.CreatePendingAsync(reviewRun, cancellationToken);

        try
        {
            reviewRun.TryStart();
            await store.SaveStateAsync(reviewRun, cancellationToken);

            var scanResult = await scanner.ScanAsync(project.NormalizedRootPath, cancellationToken);
            if (scanResult.IsFailure)
            {
                return await FailAsync(reviewRun, "ScanFailed", null, cancellationToken);
            }

            var contextResult = await contextBuilder.BuildAsync(
                project.NormalizedRootPath,
                scanResult.Value,
                cancellationToken);
            if (contextResult.IsFailure)
            {
                return await FailAsync(reviewRun, "ContextFailed", null, cancellationToken);
            }

            var agentStopwatch = Stopwatch.StartNew();
            var agentResult = await reviewAgent.RunAsync(
                new ReviewAgentInput(
                    project,
                    reviewRun.Id,
                    scanResult.Value,
                    contextResult.Value,
                    command.Policy),
                cancellationToken);
            if (agentResult.IsFailure)
            {
                var failedModelRun = CreateFailedModelRun(
                    reviewRun,
                    contextResult.Value.CharacterCount,
                    agentStopwatch.Elapsed,
                    agentResult.Error.Code);
                return await FailAsync(
                    reviewRun,
                    MapAgentFailure(agentResult.Error.Code),
                    failedModelRun,
                    cancellationToken);
            }

            var modelRun = CreateSuccessfulModelRun(reviewRun, agentResult.Value);
            var summary = JsonSerializer.Serialize(new
            {
                scanResult.Value.IncludedFileCount,
                scanResult.Value.IncludedBytes,
                ContextCharacters = contextResult.Value.CharacterCount,
                FindingCount = agentResult.Value.Findings.Count,
                RejectedFindingCount = agentResult.Value.RejectedFindings.Count,
                agentResult.Value.DiscardedDuplicateCount
            });
            reviewRun.TryComplete(timeProvider.GetUtcNow(), summary);
            await store.CompleteAsync(
                reviewRun,
                agentResult.Value.Findings,
                modelRun,
                cancellationToken);
            return Result.Success((await store.GetAsync(reviewRun.Id, cancellationToken))!);
        }
        catch (OperationCanceledException)
        {
            reviewRun.TryFail(ReviewRun.CancelledErrorCode, timeProvider.GetUtcNow());
            await store.FailAsync(reviewRun, null, CancellationToken.None);
            throw;
        }
        catch
        {
            reviewRun.TryFail("Unhandled", timeProvider.GetUtcNow());
            await store.FailAsync(reviewRun, null, CancellationToken.None);
            throw;
        }
    }

    public async Task<Result<ReviewReadModel>> GetAsync(
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        var review = reviewId == Guid.Empty
            ? null
            : await store.GetAsync(reviewId, cancellationToken);
        return review is null
            ? Result.Failure<ReviewReadModel>(ReviewWorkflowErrors.ReviewNotFound)
            : Result.Success(review);
    }

    public async Task<Result<IReadOnlyList<ReviewReadModel>>> GetHistoryAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<ReviewReadModel>>(ReviewWorkflowErrors.ProjectNotFound);
        }

        return Result.Success(await store.GetHistoryAsync(projectId, cancellationToken));
    }

    private async Task<Result<ReviewReadModel>> FailAsync(
        ReviewRun reviewRun,
        string errorCode,
        AIModelRun? modelRun,
        CancellationToken cancellationToken)
    {
        reviewRun.TryFail(errorCode, timeProvider.GetUtcNow());
        await store.FailAsync(reviewRun, modelRun, cancellationToken);
        return Result.Failure<ReviewReadModel>(ReviewWorkflowErrors.ExecutionFailed);
    }

    private AIModelRun CreateFailedModelRun(
        ReviewRun reviewRun,
        int inputCharacters,
        TimeSpan duration,
        string errorCode)
    {
        return AIModelRun.Create(
            Guid.NewGuid(),
            reviewRun.Id,
            AIModelRunPurpose.Review,
            reviewAgent.ProviderName,
            reviewRun.ModelName,
            reviewRun.PromptVersion,
            ToDurationMilliseconds(duration),
            inputCharacters,
            0,
            AIModelRunStatus.Failed,
            errorCode,
            timeProvider.GetUtcNow());
    }

    private AIModelRun CreateSuccessfulModelRun(
        ReviewRun reviewRun,
        ReviewAgentResult agentResult)
    {
        return AIModelRun.Create(
            Guid.NewGuid(),
            reviewRun.Id,
            AIModelRunPurpose.Review,
            agentResult.Provider,
            agentResult.Model,
            agentResult.PromptVersion,
            ToDurationMilliseconds(agentResult.Duration),
            agentResult.InputCharacters,
            agentResult.OutputCharacters,
            AIModelRunStatus.Succeeded,
            null,
            timeProvider.GetUtcNow());
    }

    private static int ToDurationMilliseconds(TimeSpan duration) =>
        (int)Math.Min(int.MaxValue, Math.Max(0, duration.TotalMilliseconds));

    private static string MapAgentFailure(string errorCode)
    {
        return errorCode switch
        {
            "review_agent.provider_timeout" => "ProviderTimeout",
            "review_agent.provider_rate_limit" => "ProviderRateLimit",
            "review_agent.provider_unavailable" => "ProviderUnavailable",
            _ => "InvalidModelResponse"
        };
    }
}

public static class ReviewWorkflowErrors
{
    public static readonly Error ProjectNotFound = Error.NotFound(
        "review.project_not_found",
        "The project was not found.");

    public static readonly Error ReviewNotFound = Error.NotFound(
        "review.not_found",
        "The review was not found.");

    public static readonly Error ExecutionFailed = Error.Failure(
        "review.execution_failed",
        "The review could not be completed.");
}
