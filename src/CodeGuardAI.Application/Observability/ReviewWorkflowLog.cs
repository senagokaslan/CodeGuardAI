using Microsoft.Extensions.Logging;

namespace CodeGuardAI.Application.Observability;

public static class ReviewWorkflowEventIds
{
    public const int Started = 2600;
    public const int Completed = 2601;
    public const int Failed = 2602;
}

public sealed record ReviewRunMetrics(
    Guid ReviewRunId,
    Guid ProjectId,
    string Model,
    string PromptVersion,
    long DurationMs,
    int FindingCount,
    int RejectedFindingCount);

public static partial class ReviewWorkflowLog
{
    [LoggerMessage(
        EventId = ReviewWorkflowEventIds.Started,
        Level = LogLevel.Information,
        Message = "Review {ReviewRunId} for project {ProjectId} started with model {Model} and prompt {PromptVersion}.")]
    private static partial void LogStarted(
        ILogger logger,
        Guid reviewRunId,
        Guid projectId,
        string model,
        string promptVersion);

    [LoggerMessage(
        EventId = ReviewWorkflowEventIds.Completed,
        Level = LogLevel.Information,
        Message = "Review {ReviewRunId} for project {ProjectId} completed with model {Model}, prompt {PromptVersion}, duration {DurationMs} ms, {FindingCount} findings, and {RejectedFindingCount} rejected findings.")]
    private static partial void LogCompleted(
        ILogger logger,
        Guid reviewRunId,
        Guid projectId,
        string model,
        string promptVersion,
        long durationMs,
        int findingCount,
        int rejectedFindingCount);

    [LoggerMessage(
        EventId = ReviewWorkflowEventIds.Failed,
        Level = LogLevel.Warning,
        Message = "Review {ReviewRunId} for project {ProjectId} failed with model {Model}, prompt {PromptVersion}, duration {DurationMs} ms, and error {ErrorCode}.")]
    private static partial void LogFailed(
        ILogger logger,
        Guid reviewRunId,
        Guid projectId,
        string model,
        string promptVersion,
        long durationMs,
        string errorCode);

    public static void Started(ILogger logger, ReviewRunMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(metrics);
        LogStarted(
            logger,
            metrics.ReviewRunId,
            metrics.ProjectId,
            metrics.Model,
            metrics.PromptVersion);
    }

    public static void Completed(ILogger logger, ReviewRunMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(metrics);
        LogCompleted(
            logger,
            metrics.ReviewRunId,
            metrics.ProjectId,
            metrics.Model,
            metrics.PromptVersion,
            metrics.DurationMs,
            metrics.FindingCount,
            metrics.RejectedFindingCount);
    }

    public static void Failed(ILogger logger, ReviewRunMetrics metrics, string errorCode)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        LogFailed(
            logger,
            metrics.ReviewRunId,
            metrics.ProjectId,
            metrics.Model,
            metrics.PromptVersion,
            metrics.DurationMs,
            errorCode);
    }
}
