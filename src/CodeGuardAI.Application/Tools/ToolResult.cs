namespace CodeGuardAI.Application.Tools;

public sealed record ToolResult<T> where T : notnull
{
    private readonly T? _value;

    private ToolResult(bool isSuccess, T? value, string? errorCode, TimeSpan duration, bool truncated)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (isSuccess != string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Success and error code must be consistent.", nameof(errorCode));
        }

        IsSuccess = isSuccess;
        _value = value;
        ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim();
        Duration = duration;
        Truncated = truncated;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed tool result does not contain a value.");
    public string? ErrorCode { get; }
    public TimeSpan Duration { get; }
    public bool Truncated { get; }

    public static ToolResult<T> Success(T value, TimeSpan duration, bool truncated = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ToolResult<T>(true, value, null, duration, truncated);
    }

    public static ToolResult<T> Failure(string errorCode, TimeSpan duration, bool truncated = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return new ToolResult<T>(false, default, errorCode, duration, truncated);
    }
}

public sealed record ToolExecutionOptions
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public ToolExecutionOptions(TimeSpan timeout, Guid? reviewRunId = null)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Tool timeout must be positive and no longer than ten minutes.");
        }

        if (reviewRunId == Guid.Empty)
        {
            throw new ArgumentException("Review run id cannot be empty.", nameof(reviewRunId));
        }

        Timeout = timeout;
        ReviewRunId = reviewRunId;
    }

    public TimeSpan Timeout { get; }
    public Guid? ReviewRunId { get; }
    public static ToolExecutionOptions Default { get; } = new(DefaultTimeout);
}

public static class ToolErrorCodes
{
    public const string AuthorizationDenied = "tool.authorization_denied";
    public const string Timeout = "tool.timeout";
    public const string AuditFailed = "tool.audit_failed";
    public const string ExecutionFailed = "tool.execution_failed";
}
