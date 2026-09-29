using CodeGuardAI.Domain.Common;

namespace CodeGuardAI.Domain.Observability;

public sealed class AIModelRun
{
    private AIModelRun(
        Guid id,
        Guid reviewRunId,
        AIModelRunPurpose purpose,
        string provider,
        string modelName,
        string promptVersion,
        int durationMs,
        int inputChars,
        int outputChars,
        AIModelRunStatus status,
        string? errorType,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ReviewRunId = reviewRunId;
        Purpose = purpose;
        Provider = provider;
        ModelName = modelName;
        PromptVersion = promptVersion;
        DurationMs = durationMs;
        InputChars = inputChars;
        OutputChars = outputChars;
        Status = status;
        ErrorType = errorType;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }
    public Guid ReviewRunId { get; }
    public AIModelRunPurpose Purpose { get; }
    public string Provider { get; }
    public string ModelName { get; }
    public string PromptVersion { get; }
    public int DurationMs { get; }
    public int InputChars { get; }
    public int OutputChars { get; }
    public AIModelRunStatus Status { get; }
    public string? ErrorType { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public static AIModelRun Create(
        Guid id,
        Guid reviewRunId,
        AIModelRunPurpose purpose,
        string provider,
        string modelName,
        string promptVersion,
        int durationMs,
        int inputChars,
        int outputChars,
        AIModelRunStatus status,
        string? errorType,
        DateTimeOffset createdAtUtc)
    {
        var validatedStatus = DomainGuard.DefinedEnum(status, nameof(status));
        var normalizedErrorType = string.IsNullOrWhiteSpace(errorType) ? null : errorType.Trim();
        EnsureErrorConsistency(validatedStatus, normalizedErrorType, nameof(errorType));

        return new AIModelRun(
            DomainGuard.NotEmpty(id, nameof(id)),
            DomainGuard.NotEmpty(reviewRunId, nameof(reviewRunId)),
            DomainGuard.DefinedEnum(purpose, nameof(purpose)),
            DomainGuard.Required(provider, nameof(provider)),
            DomainGuard.Required(modelName, nameof(modelName)),
            DomainGuard.Required(promptVersion, nameof(promptVersion)),
            DomainGuard.NonNegative(durationMs, nameof(durationMs)),
            DomainGuard.NonNegative(inputChars, nameof(inputChars)),
            DomainGuard.NonNegative(outputChars, nameof(outputChars)),
            validatedStatus,
            normalizedErrorType,
            DomainGuard.Utc(createdAtUtc, nameof(createdAtUtc)));
    }

    private static void EnsureErrorConsistency(
        AIModelRunStatus status,
        string? errorType,
        string parameterName)
    {
        if (status == AIModelRunStatus.Succeeded && errorType is not null)
        {
            throw new ArgumentException("A successful model run cannot contain an error type.", parameterName);
        }

        if (status == AIModelRunStatus.Failed && errorType is null)
        {
            throw new ArgumentException("A failed model run requires an error type.", parameterName);
        }
    }
}
