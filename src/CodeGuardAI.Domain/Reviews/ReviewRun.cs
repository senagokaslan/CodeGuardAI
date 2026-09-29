using CodeGuardAI.Domain.Common;

namespace CodeGuardAI.Domain.Reviews;

public sealed class ReviewRun
{
    public const string CancelledErrorCode = "Cancelled";

    private ReviewRun(
        Guid id,
        Guid projectId,
        string modelName,
        string promptVersion,
        DateTimeOffset startedAtUtc)
    {
        Id = id;
        ProjectId = projectId;
        ModelName = modelName;
        PromptVersion = promptVersion;
        StartedAtUtc = startedAtUtc;
        Status = ReviewStatus.Running;
    }

    public Guid Id { get; }

    public Guid ProjectId { get; }

    public ReviewStatus Status { get; private set; }

    public string ModelName { get; }

    public string PromptVersion { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ScanSummaryJson { get; private set; }

    public static ReviewRun Start(
        Guid id,
        Guid projectId,
        string modelName,
        string promptVersion,
        DateTimeOffset startedAtUtc)
    {
        return new ReviewRun(
            DomainGuard.NotEmpty(id, nameof(id)),
            DomainGuard.NotEmpty(projectId, nameof(projectId)),
            DomainGuard.Required(modelName, nameof(modelName)),
            DomainGuard.Required(promptVersion, nameof(promptVersion)),
            DomainGuard.Utc(startedAtUtc, nameof(startedAtUtc)));
    }

    public bool TryComplete(DateTimeOffset completedAtUtc, string? scanSummaryJson = null)
    {
        if (Status != ReviewStatus.Running)
        {
            return false;
        }

        EnsureCompletionTime(completedAtUtc);
        Status = ReviewStatus.Completed;
        CompletedAtUtc = completedAtUtc;
        ScanSummaryJson = string.IsNullOrWhiteSpace(scanSummaryJson) ? null : scanSummaryJson;
        return true;
    }

    public bool TryFail(string errorCode, DateTimeOffset completedAtUtc)
    {
        if (Status != ReviewStatus.Running)
        {
            return false;
        }

        var validatedErrorCode = DomainGuard.Required(errorCode, nameof(errorCode));
        EnsureCompletionTime(completedAtUtc);
        Status = ReviewStatus.Failed;
        ErrorCode = validatedErrorCode;
        CompletedAtUtc = completedAtUtc;
        return true;
    }

    private void EnsureCompletionTime(DateTimeOffset completedAtUtc)
    {
        DomainGuard.Utc(completedAtUtc, nameof(completedAtUtc));

        if (completedAtUtc < StartedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAtUtc),
                completedAtUtc,
                "Completion cannot precede the start time.");
        }
    }
}
