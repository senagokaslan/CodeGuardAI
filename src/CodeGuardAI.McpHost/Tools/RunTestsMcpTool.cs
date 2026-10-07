using System.ComponentModel;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Infrastructure.Tools;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace CodeGuardAI.McpHost.Tools;

[McpServerToolType]
public sealed class RunTestsMcpTool(
    IProjectQueries projectQueries,
    ITestRunnerTool testRunner,
    IOptions<TestRunnerOptions> options)
{
    public const string ToolName = "run_tests";
    public const string TestsPassedOutcome = "tests_passed";
    public const string TestsFailedOutcome = "tests_failed";
    public const string ToolErrorOutcome = "tool_error";
    public const string RepositoryNotFoundCode = "mcp.repository_not_found";
    public const string InvalidRequestCode = "mcp.request_invalid";
    public const string UnexpectedFailureCode = "mcp.run_tests_failed";

    private readonly TestRunnerOptions _options = options.Value;

    [McpServerTool(Name = ToolName, Destructive = true, UseStructuredContent = true)]
    [Description("Runs dotnet test for one allowlisted project in a registered repository through CodeGuard's existing safe test runner.")]
    public async Task<RunTestsMcpResponse> RunTestsAsync(
        [Description("The registered CodeGuard project/repository identifier.")] Guid repositoryId,
        [Description("A repository-relative .csproj target. Commands and extra arguments are not accepted.")] string target,
        CancellationToken cancellationToken)
    {
        if (repositoryId == Guid.Empty || string.IsNullOrWhiteSpace(target))
        {
            return RunTestsMcpResponse.ToolError(InvalidRequestCode);
        }

        try
        {
            var project = await projectQueries.GetByIdAsync(repositoryId, cancellationToken);
            if (project is null)
            {
                return RunTestsMcpResponse.ToolError(RepositoryNotFoundCode);
            }

            TestRunnerToolInput input;
            try
            {
                input = new TestRunnerToolInput(
                    project.RepositoryPath,
                    target,
                    TestRunnerKind.DotNet,
                    new ToolExecutionOptions(_options.DefaultTimeout));
            }
            catch (ArgumentException)
            {
                return RunTestsMcpResponse.ToolError(InvalidRequestCode);
            }

            var result = await testRunner.ExecuteAsync(input, cancellationToken);
            if (result.IsFailure)
            {
                return RunTestsMcpResponse.ToolError(result.ErrorCode!, result.Truncated);
            }

            return RunTestsMcpResponse.Completed(result.Value, result.Truncated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return RunTestsMcpResponse.ToolError(UnexpectedFailureCode);
        }
    }
}

public sealed record RunTestsMcpResponse(
    bool ToolSucceeded,
    string Outcome,
    int? ExitCode,
    int PassedCount,
    int FailedCount,
    int SkippedCount,
    string? Output,
    string? ErrorCode,
    bool Truncated)
{
    public static RunTestsMcpResponse Completed(TestRunnerOutput output, bool truncated) =>
        new(
            true,
            output.ExitCode == 0
                ? RunTestsMcpTool.TestsPassedOutcome
                : RunTestsMcpTool.TestsFailedOutcome,
            output.ExitCode,
            output.PassedCount,
            output.FailedCount,
            output.SkippedCount,
            output.Output,
            null,
            truncated);

    public static RunTestsMcpResponse ToolError(string errorCode, bool truncated = false) =>
        new(
            false,
            RunTestsMcpTool.ToolErrorOutcome,
            null,
            0,
            0,
            0,
            null,
            errorCode,
            truncated);
}
