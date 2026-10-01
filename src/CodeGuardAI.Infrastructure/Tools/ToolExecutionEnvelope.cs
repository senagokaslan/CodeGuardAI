using System.Diagnostics;
using CodeGuardAI.Application.Tools;
using Microsoft.Extensions.Logging;

namespace CodeGuardAI.Infrastructure.Tools;

internal sealed record ToolOperationResult<T>(
    T? Value,
    string? ErrorCode,
    bool Truncated,
    string RedactedOutputSummary) where T : notnull
{
    public bool IsSuccess => ErrorCode is null;

    public static ToolOperationResult<T> Success(
        T value,
        string redactedOutputSummary,
        bool truncated = false) =>
        new(value, null, truncated, redactedOutputSummary);

    public static ToolOperationResult<T> Failure(string errorCode, bool truncated = false) =>
        new(default, errorCode, truncated, $"success=false; truncated={truncated.ToString().ToLowerInvariant()}");
}

public sealed class ToolExecutionEnvelope(
    IToolAuthorizationPolicy authorizationPolicy,
    IToolExecutionWriter executionWriter,
    TimeProvider timeProvider)
{
    internal async Task<ToolResult<T>> ExecuteAsync<T>(
        string toolName,
        string redactedInputSummary,
        ToolExecutionOptions options,
        ILogger logger,
        Func<CancellationToken, Task<ToolOperationResult<T>>> operation,
        CancellationToken cancellationToken) where T : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["ToolName"] = toolName,
            ["ReviewRunId"] = options.ReviewRunId,
            ["InputSummary"] = redactedInputSummary
        });

        var authorizationRequest = new ToolAuthorizationRequest(
            toolName,
            options.ReviewRunId,
            redactedInputSummary);
        bool isAuthorized;
        try
        {
            isAuthorized = await authorizationPolicy.IsAuthorizedAsync(
                authorizationRequest,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            isAuthorized = false;
        }

        if (!isAuthorized)
        {
            return await CompleteAsync<T>(
                toolName,
                options,
                redactedInputSummary,
                ToolExecutionOutcome.Failed,
                ToolErrorCodes.AuthorizationDenied,
                truncated: false,
                "success=false; authorized=false",
                stopwatch.Elapsed,
                logger,
                cancellationToken);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        ToolOperationResult<T> operationResult;
        ToolExecutionOutcome outcome;
        try
        {
            operationResult = await operation(timeout.Token);
            outcome = operationResult.IsSuccess
                ? ToolExecutionOutcome.Succeeded
                : ToolExecutionOutcome.Failed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            operationResult = ToolOperationResult<T>.Failure(ToolErrorCodes.Timeout);
            outcome = ToolExecutionOutcome.TimedOut;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            operationResult = ToolOperationResult<T>.Failure(ToolErrorCodes.ExecutionFailed);
            outcome = ToolExecutionOutcome.Failed;
        }

        var duration = stopwatch.Elapsed;
        var errorCode = operationResult.ErrorCode;
        var recorded = await TryRecordAsync(
            toolName,
            options.ReviewRunId,
            outcome,
            duration,
            redactedInputSummary,
            operationResult.RedactedOutputSummary,
            errorCode,
            logger,
            cancellationToken);
        if (!recorded)
        {
            return ToolResult<T>.Failure(ToolErrorCodes.AuditFailed, duration);
        }

        logger.LogInformation(
            "Tool execution completed with outcome {Outcome}, error code {ErrorCode}, duration {DurationMs} ms, and truncated {Truncated}.",
            outcome,
            errorCode,
            ToMilliseconds(duration),
            operationResult.Truncated);
        return operationResult.IsSuccess
            ? ToolResult<T>.Success(operationResult.Value!, duration, operationResult.Truncated)
            : ToolResult<T>.Failure(errorCode!, duration, operationResult.Truncated);
    }

    private async Task<ToolResult<T>> CompleteAsync<T>(
        string toolName,
        ToolExecutionOptions options,
        string inputSummary,
        ToolExecutionOutcome outcome,
        string errorCode,
        bool truncated,
        string outputSummary,
        TimeSpan duration,
        ILogger logger,
        CancellationToken cancellationToken) where T : notnull
    {
        var recorded = await TryRecordAsync(
            toolName,
            options.ReviewRunId,
            outcome,
            duration,
            inputSummary,
            outputSummary,
            errorCode,
            logger,
            cancellationToken);
        return ToolResult<T>.Failure(
            recorded ? errorCode : ToolErrorCodes.AuditFailed,
            duration,
            truncated);
    }

    private async Task<bool> TryRecordAsync(
        string toolName,
        Guid? reviewRunId,
        ToolExecutionOutcome outcome,
        TimeSpan duration,
        string inputSummary,
        string outputSummary,
        string? errorCode,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await executionWriter.RecordAsync(
                new ToolExecutionRecord(
                    Guid.NewGuid(),
                    reviewRunId,
                    toolName,
                    outcome,
                    ToMilliseconds(duration),
                    inputSummary,
                    outputSummary,
                    errorCode,
                    timeProvider.GetUtcNow()),
                cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("Tool audit persistence failed for {ToolName}.", toolName);
            return false;
        }
    }

    private static int ToMilliseconds(TimeSpan duration) =>
        (int)Math.Min(int.MaxValue, Math.Max(0, duration.TotalMilliseconds));
}

public sealed class InternalToolAuthorizationPolicy : IToolAuthorizationPolicy
{
    private static readonly HashSet<string> AllowedTools =
    [
        ToolNames.FileRead,
        ToolNames.RepositoryScan,
        ToolNames.TestRunner
    ];

    public ValueTask<bool> IsAuthorizedAsync(
        ToolAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(AllowedTools.Contains(request.ToolName));
    }
}

internal sealed class NullToolExecutionWriter : IToolExecutionWriter
{
    public Task RecordAsync(ToolExecutionRecord execution, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
