using System.ComponentModel;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Application.Tools;
using ModelContextProtocol.Server;

namespace CodeGuardAI.McpHost.Tools;

[McpServerToolType]
public sealed class ReadFileMcpTool(
    IProjectQueries projectQueries,
    IFileReadTool fileReadTool)
{
    public const string ToolName = "read_file";
    public const string RepositoryNotFoundCode = "mcp.repository_not_found";
    public const string InvalidRequestCode = "mcp.request_invalid";
    public const string UnexpectedFailureCode = "mcp.read_file_failed";

    [McpServerTool(Name = ToolName, ReadOnly = true, UseStructuredContent = true)]
    [Description("Reads one UTF-8 text file from a registered repository through CodeGuard's existing safe file-read policy.")]
    public async Task<ReadFileMcpResponse> ReadFileAsync(
        [Description("The registered CodeGuard project/repository identifier.")] Guid repositoryId,
        [Description("A path relative to the registered repository root.")] string relativePath,
        CancellationToken cancellationToken)
    {
        if (repositoryId == Guid.Empty || string.IsNullOrWhiteSpace(relativePath))
        {
            return ReadFileMcpResponse.Failure(InvalidRequestCode, truncated: false);
        }

        try
        {
            var project = await projectQueries.GetByIdAsync(repositoryId, cancellationToken);
            if (project is null)
            {
                return ReadFileMcpResponse.Failure(RepositoryNotFoundCode, truncated: false);
            }

            var result = await fileReadTool.ExecuteAsync(
                new FileReadToolInput(project.RepositoryPath, relativePath),
                cancellationToken);

            return result.IsSuccess
                ? ReadFileMcpResponse.Success(
                    result.Value.RelativePath,
                    result.Value.Content,
                    result.Truncated)
                : ReadFileMcpResponse.Failure(result.ErrorCode!, result.Truncated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ReadFileMcpResponse.Failure(UnexpectedFailureCode, truncated: false);
        }
    }
}

public sealed record ReadFileMcpResponse(
    bool IsSuccess,
    string? RelativePath,
    string? Content,
    string? ErrorCode,
    bool Truncated)
{
    public static ReadFileMcpResponse Success(
        string relativePath,
        string content,
        bool truncated) =>
        new(true, relativePath, content, null, truncated);

    public static ReadFileMcpResponse Failure(string errorCode, bool truncated) =>
        new(false, null, null, errorCode, truncated);
}
