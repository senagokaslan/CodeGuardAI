using CodeGuardAI.Domain.Common;

namespace CodeGuardAI.Domain.Observability;

public sealed class ToolExecution
{
    private ToolExecution(
        Guid id,
        Guid? reviewRunId,
        string toolName,
        ToolExecutionStatus status,
        int durationMs,
        string inputSummary,
        string outputSummary,
        string? errorType,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ReviewRunId = reviewRunId;
        ToolName = toolName;
        Status = status;
        DurationMs = durationMs;
        InputSummary = inputSummary;
        OutputSummary = outputSummary;
        ErrorType = errorType;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }
    public Guid? ReviewRunId { get; }
    public string ToolName { get; }
    public ToolExecutionStatus Status { get; }
    public int DurationMs { get; }
    public string InputSummary { get; }
    public string OutputSummary { get; }
    public string? ErrorType { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public static ToolExecution Create(
        Guid id,
        Guid? reviewRunId,
        string toolName,
        ToolExecutionStatus status,
        int durationMs,
        string inputSummary,
        string outputSummary,
        string? errorType,
        DateTimeOffset createdAtUtc)
    {
        if (reviewRunId == Guid.Empty)
        {
            throw new ArgumentException("When supplied, the review run identifier cannot be empty.", nameof(reviewRunId));
        }

        var validatedStatus = DomainGuard.DefinedEnum(status, nameof(status));
        var normalizedErrorType = string.IsNullOrWhiteSpace(errorType) ? null : errorType.Trim();
        EnsureErrorConsistency(validatedStatus, normalizedErrorType, nameof(errorType));

        return new ToolExecution(
            DomainGuard.NotEmpty(id, nameof(id)),
            reviewRunId,
            DomainGuard.Required(toolName, nameof(toolName)),
            validatedStatus,
            DomainGuard.NonNegative(durationMs, nameof(durationMs)),
            DomainGuard.Required(inputSummary, nameof(inputSummary)),
            DomainGuard.Required(outputSummary, nameof(outputSummary)),
            normalizedErrorType,
            DomainGuard.Utc(createdAtUtc, nameof(createdAtUtc)));
    }

    private static void EnsureErrorConsistency(
        ToolExecutionStatus status,
        string? errorType,
        string parameterName)
    {
        if (status == ToolExecutionStatus.Succeeded && errorType is not null)
        {
            throw new ArgumentException("A successful tool execution cannot contain an error type.", parameterName);
        }

        if (status != ToolExecutionStatus.Succeeded && errorType is null)
        {
            throw new ArgumentException("A failed or timed-out tool execution requires an error type.", parameterName);
        }
    }
}
