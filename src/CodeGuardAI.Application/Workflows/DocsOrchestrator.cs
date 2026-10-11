using System.Diagnostics;
using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Documentation;
using CodeGuardAI.Application.Prompts;
using CodeGuardAI.Domain.Documentation;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;
using Microsoft.Extensions.Logging;

namespace CodeGuardAI.Application.Workflows;

public interface IDocsOrchestrator
{
    Task<Result<DocumentationReadModel>> CreateAsync(
        CreateDocumentationCommand command,
        CancellationToken cancellationToken);

    Task<Result<DocumentationReadModel>> GetAsync(
        Guid reviewId,
        CancellationToken cancellationToken);
}

public interface IDocsWorkflowStore
{
    Task<DocsGenerationSource?> GetSourceAsync(Guid reviewId, CancellationToken cancellationToken);

    Task<bool> HasCompletedReportAsync(Guid reviewId, CancellationToken cancellationToken);

    Task<bool> TryCompleteAsync(
        DocumentationReport report,
        AIModelRun modelRun,
        CancellationToken cancellationToken);

    Task SaveFailedModelRunAsync(AIModelRun modelRun, CancellationToken cancellationToken);

    Task<DocumentationReadModel?> GetAsync(Guid reviewId, CancellationToken cancellationToken);
}

public sealed record DocsGenerationSource(
    ReviewRun ReviewRun,
    string ProjectName,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<TestCase> TestCases);

public sealed record CreateDocumentationCommand(Guid ReviewId, DocsAgentPolicy Policy);

public sealed record DocumentationReadModel(
    Guid Id,
    Guid ReviewId,
    Guid ModelRunId,
    string Title,
    string MarkdownContent,
    DateTimeOffset CreatedAtUtc);

public sealed class DocsOrchestrator(
    IDocsWorkflowStore store,
    IDocsAgent docsAgent,
    MarkdownReportRenderer renderer,
    TimeProvider timeProvider,
    ILogger<DocsOrchestrator> logger) : IDocsOrchestrator
{
    public const int MaxGenerationSteps = 6;

    public async Task<Result<DocumentationReadModel>> CreateAsync(
        CreateDocumentationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var steps = new WorkflowStepBudget(MaxGenerationSteps);
        EnterStep(steps, "LoadSource");
        var source = command.ReviewId == Guid.Empty
            ? null
            : await store.GetSourceAsync(command.ReviewId, cancellationToken);
        if (source is null)
        {
            return Result.Failure<DocumentationReadModel>(DocumentationWorkflowErrors.ReviewNotFound);
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = source.ReviewRun.Id,
            ["ReviewRunId"] = source.ReviewRun.Id,
            ["ProjectId"] = source.ReviewRun.ProjectId
        });

        EnterStep(steps, "ValidateCompletedReview");
        if (source.ReviewRun.Status != ReviewStatus.Completed)
        {
            return Result.Failure<DocumentationReadModel>(
                DocumentationWorkflowErrors.ReviewNotCompleted);
        }

        EnterStep(steps, "CheckIdempotency");
        if (await store.HasCompletedReportAsync(command.ReviewId, cancellationToken))
        {
            return Result.Failure<DocumentationReadModel>(
                DocumentationWorkflowErrors.AlreadyGenerated);
        }

        EnterStep(steps, "RunAgent");
        var stopwatch = Stopwatch.StartNew();
        Result<DocsAgentResult> agentResult;
        try
        {
            agentResult = await docsAgent.RunAsync(
                new DocsAgentInput(
                    source.ReviewRun,
                    source.ProjectName,
                    source.Findings,
                    source.TestCases,
                    command.Policy),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await PersistFailedModelRunAsync(
                source.ReviewRun.Id,
                command.Policy,
                stopwatch.Elapsed,
                "Cancelled",
                CancellationToken.None);
            throw;
        }

        if (agentResult.IsFailure)
        {
            EnterStep(steps, "PersistTerminal");
            await PersistFailedModelRunAsync(
                source.ReviewRun.Id,
                command.Policy,
                stopwatch.Elapsed,
                agentResult.Error.Code,
                cancellationToken);
            logger.LogWarning(
                "Documentation generation failed with category {ErrorCode}.",
                agentResult.Error.Code);
            return Result.Failure<DocumentationReadModel>(
                DocumentationWorkflowErrors.GenerationFailed);
        }

        EnterStep(steps, "RenderMarkdown");
        Result<string> renderResult;
        try
        {
            renderResult = renderer.Render(
                agentResult.Value.Report,
                command.Policy.MaxReportCharacters,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await PersistFailedModelRunAsync(
                source.ReviewRun.Id,
                command.Policy,
                stopwatch.Elapsed,
                "Cancelled",
                CancellationToken.None);
            throw;
        }

        if (renderResult.IsFailure)
        {
            EnterStep(steps, "PersistTerminal");
            await PersistFailedModelRunAsync(
                source.ReviewRun.Id,
                command.Policy,
                stopwatch.Elapsed,
                renderResult.Error.Code,
                cancellationToken);
            return Result.Failure<DocumentationReadModel>(
                DocumentationWorkflowErrors.GenerationFailed);
        }

        var modelRun = CreateSuccessfulModelRun(agentResult.Value);
        var report = DocumentationReport.Create(
            Guid.NewGuid(),
            source.ReviewRun.Id,
            modelRun.Id,
            agentResult.Value.Report.Title,
            renderResult.Value,
            timeProvider.GetUtcNow());

        EnterStep(steps, "PersistTerminal");
        var completed = await store.TryCompleteAsync(report, modelRun, cancellationToken);
        if (!completed)
        {
            logger.LogWarning(
                "Concurrent documentation generation rejected for review {ReviewRunId}.",
                source.ReviewRun.Id);
            return Result.Failure<DocumentationReadModel>(
                DocumentationWorkflowErrors.AlreadyGenerated);
        }

        logger.LogInformation(
            "Documentation generation completed with {FindingCount} findings and {TestCaseCount} test cases.",
            agentResult.Value.FindingCount,
            agentResult.Value.TestCaseCount);
        return Result.Success(ToReadModel(report));
    }

    public async Task<Result<DocumentationReadModel>> GetAsync(
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        var report = reviewId == Guid.Empty
            ? null
            : await store.GetAsync(reviewId, cancellationToken);
        return report is null
            ? Result.Failure<DocumentationReadModel>(DocumentationWorkflowErrors.ReportNotFound)
            : Result.Success(report);
    }

    private async Task PersistFailedModelRunAsync(
        Guid reviewId,
        DocsAgentPolicy policy,
        TimeSpan duration,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var modelRun = AIModelRun.Create(
            Guid.NewGuid(),
            reviewId,
            AIModelRunPurpose.Documentation,
            docsAgent.ProviderName,
            policy.Model,
            DocsPromptRegistry.CurrentVersion,
            ToMilliseconds(duration),
            0,
            0,
            AIModelRunStatus.Failed,
            errorCode,
            timeProvider.GetUtcNow());
        await store.SaveFailedModelRunAsync(modelRun, cancellationToken);
    }

    private AIModelRun CreateSuccessfulModelRun(DocsAgentResult result) =>
        AIModelRun.Create(
            Guid.NewGuid(),
            result.ReviewRunId,
            AIModelRunPurpose.Documentation,
            result.Provider,
            result.Model,
            result.PromptVersion,
            ToMilliseconds(result.Duration),
            result.InputCharacters,
            result.OutputCharacters,
            AIModelRunStatus.Succeeded,
            null,
            timeProvider.GetUtcNow());

    private static DocumentationReadModel ToReadModel(DocumentationReport report) =>
        new(
            report.Id,
            report.ReviewRunId,
            report.ModelRunId,
            report.Title,
            report.MarkdownContent,
            report.CreatedAtUtc);

    private static int ToMilliseconds(TimeSpan duration) =>
        (int)Math.Min(int.MaxValue, Math.Max(0, duration.TotalMilliseconds));

    private void EnterStep(WorkflowStepBudget budget, string step)
    {
        budget.Enter(step);
        logger.LogDebug(
            "Documentation workflow entered step {Step} ({StepNumber}/{MaxSteps}).",
            step,
            budget.StepsUsed,
            budget.MaxSteps);
    }
}

public static class DocumentationWorkflowErrors
{
    public static readonly Error ReviewNotFound = Error.NotFound(
        "documentation.review_not_found",
        "The review was not found.");

    public static readonly Error ReportNotFound = Error.NotFound(
        "documentation.report_not_found",
        "The documentation report was not found.");

    public static readonly Error ReviewNotCompleted = Error.Conflict(
        "documentation.review_not_completed",
        "Documentation requires a completed review.");

    public static readonly Error AlreadyGenerated = Error.Conflict(
        "documentation.already_generated",
        "Documentation already exists for this review.");

    public static readonly Error GenerationFailed = Error.Failure(
        "documentation.generation_failed",
        "The documentation report could not be generated.");
}
