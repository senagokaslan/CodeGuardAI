using CodeGuardAI.Application.Repositories;

namespace CodeGuardAI.Application.Tools;

public interface IRepositoryScanTool
{
    Task<ToolResult<ScanManifest>> ExecuteAsync(RepositoryScanToolInput input, CancellationToken cancellationToken);
}

public sealed record RepositoryScanToolInput
{
    public RepositoryScanToolInput(string repositoryRoot, ToolExecutionOptions? execution = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        RepositoryRoot = repositoryRoot;
        Execution = execution ?? ToolExecutionOptions.Default;
    }

    public string RepositoryRoot { get; }
    public ToolExecutionOptions Execution { get; }
    public string ToRedactedAuditSummary() => "repository=[redacted]";
}
