namespace CodeGuardAI.Application.Tools;

public static class ToolNames
{
    public const string FileRead = "file-read";
    public const string RepositoryScan = "repository-scan";
    public const string TestRunner = "test-runner";
}

public sealed record ToolAuthorizationRequest(
    string ToolName,
    Guid? ReviewRunId,
    string RedactedInputSummary);

public interface IToolAuthorizationPolicy
{
    ValueTask<bool> IsAuthorizedAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken);
}

public sealed record ToolExecutionRecord(
    Guid Id,
    Guid? ReviewRunId,
    string ToolName,
    ToolExecutionOutcome Outcome,
    int DurationMilliseconds,
    string RedactedInputSummary,
    string RedactedOutputSummary,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc);

public enum ToolExecutionOutcome
{
    Succeeded = 1,
    Failed = 2,
    TimedOut = 3
}

public interface IToolExecutionWriter
{
    Task RecordAsync(ToolExecutionRecord execution, CancellationToken cancellationToken);
}
