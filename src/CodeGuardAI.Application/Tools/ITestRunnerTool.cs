namespace CodeGuardAI.Application.Tools;

public interface ITestRunnerTool
{
    Task<ToolResult<TestRunnerOutput>> ExecuteAsync(TestRunnerToolInput input, CancellationToken cancellationToken);
}

public enum TestRunnerKind
{
    DotNet = 1
}

public sealed record TestRunnerToolInput
{
    public TestRunnerToolInput(
        string repositoryRoot,
        string relativeProjectPath,
        TestRunnerKind runner,
        ToolExecutionOptions? execution = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeProjectPath);
        if (!Enum.IsDefined(runner))
        {
            throw new ArgumentOutOfRangeException(nameof(runner));
        }

        if (Path.IsPathFullyQualified(relativeProjectPath) ||
            !Path.GetExtension(relativeProjectPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            relativeProjectPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException(
                "Test project must be a repository-relative .csproj path without traversal.",
                nameof(relativeProjectPath));
        }

        RepositoryRoot = repositoryRoot;
        RelativeProjectPath = relativeProjectPath;
        Runner = runner;
        Execution = execution ?? ToolExecutionOptions.Default;
    }

    public string RepositoryRoot { get; }
    public string RelativeProjectPath { get; }
    public TestRunnerKind Runner { get; }
    public ToolExecutionOptions Execution { get; }
    public string ToRedactedAuditSummary() =>
        $"repository=[redacted]; project=[redacted]; runner={Runner}";
}

public sealed record TestRunnerOutput(
    int ExitCode,
    int PassedCount,
    int FailedCount,
    int SkippedCount,
    string Output);
