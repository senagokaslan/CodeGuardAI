using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Application.Tools;
using Microsoft.Extensions.Logging;

namespace CodeGuardAI.Infrastructure.Tools;

public sealed class RepositoryScanTool(
    IRepositoryScanner scanner,
    ToolExecutionEnvelope envelope,
    ILogger<RepositoryScanTool> logger) : IRepositoryScanTool
{
    public Task<ToolResult<ScanManifest>> ExecuteAsync(
        RepositoryScanToolInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return envelope.ExecuteAsync(
            ToolNames.RepositoryScan,
            input.ToRedactedAuditSummary(),
            input.Execution,
            logger,
            async token =>
            {
                var result = await scanner.ScanAsync(input.RepositoryRoot, token);
                if (result.IsFailure)
                {
                    return ToolOperationResult<ScanManifest>.Failure(result.Error.Code);
                }

                var truncated = result.Value.Entries.Any(entry => entry.SkipReason is
                    ScanSkipReason.FileCountLimit or ScanSkipReason.TotalBytesLimit);
                return ToolOperationResult<ScanManifest>.Success(
                    result.Value,
                    $"success=true; files={result.Value.IncludedFileCount}; bytes={result.Value.IncludedBytes}; truncated={truncated.ToString().ToLowerInvariant()}",
                    truncated);
            },
            cancellationToken);
    }
}
