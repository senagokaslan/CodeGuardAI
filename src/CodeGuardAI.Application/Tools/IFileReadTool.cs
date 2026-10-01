namespace CodeGuardAI.Application.Tools;

public interface IFileReadTool
{
    Task<ToolResult<FileReadContent>> ExecuteAsync(FileReadToolInput input, CancellationToken cancellationToken);
}

public sealed record FileReadToolInput
{
    public FileReadToolInput(
        string repositoryRoot,
        string relativePath,
        ToolExecutionOptions? execution = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        RepositoryRoot = repositoryRoot;
        RelativePath = relativePath;
        Execution = execution ?? ToolExecutionOptions.Default;
    }

    public string RepositoryRoot { get; }
    public string RelativePath { get; }
    public ToolExecutionOptions Execution { get; }
    public string ToRedactedAuditSummary() =>
        $"repository=[redacted]; path=[redacted]; kind={GetSafeKind(RelativePath)}";

    private static string GetSafeKind(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".csproj" => "msbuild",
            ".json" => "json",
            _ => "other"
        };
}

public sealed record FileReadContent(string RelativePath, string Content);

public static class FileReadErrorCodes
{
    public const string InvalidPath = "file_read.path_invalid";
    public const string NotFound = "file_read.not_found";
    public const string Binary = "file_read.binary";
    public const string InvalidEncoding = "file_read.invalid_utf8";
    public const string TooLarge = "file_read.too_large";
    public const string Failed = "file_read.failed";
}

public static class FileReadErrors
{
    public static readonly Common.Error InvalidPath = Common.Error.Validation(
        FileReadErrorCodes.InvalidPath,
        "The requested repository path is not safe to read.");
    public static readonly Common.Error NotFound = Common.Error.NotFound(
        FileReadErrorCodes.NotFound,
        "The requested repository file was not found.");
    public static readonly Common.Error Binary = Common.Error.Validation(
        FileReadErrorCodes.Binary,
        "Binary repository files cannot be added to context.");
    public static readonly Common.Error InvalidEncoding = Common.Error.Validation(
        FileReadErrorCodes.InvalidEncoding,
        "Repository text files must contain valid UTF-8.");
    public static readonly Common.Error TooLarge = Common.Error.Validation(
        FileReadErrorCodes.TooLarge,
        "The repository file exceeds the safe read limit.");
    public static readonly Common.Error Failed = Common.Error.Failure(
        FileReadErrorCodes.Failed,
        "The repository file could not be read.");
}
