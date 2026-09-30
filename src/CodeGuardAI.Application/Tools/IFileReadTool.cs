using CodeGuardAI.Application.Common;

namespace CodeGuardAI.Application.Tools;

public interface IFileReadTool
{
    Task<Result<FileReadContent>> ReadAsync(
        string repositoryRoot,
        string relativePath,
        CancellationToken cancellationToken);
}

public sealed record FileReadContent(string RelativePath, string Content);

public static class FileReadErrors
{
    public static readonly Error InvalidPath = Error.Validation(
        "file_read.path_invalid",
        "The requested repository path is not safe to read.");

    public static readonly Error NotFound = Error.NotFound(
        "file_read.not_found",
        "The requested repository file was not found.");

    public static readonly Error Binary = Error.Validation(
        "file_read.binary",
        "Binary repository files cannot be added to context.");

    public static readonly Error InvalidEncoding = Error.Validation(
        "file_read.invalid_utf8",
        "Repository text files must contain valid UTF-8.");

    public static readonly Error TooLarge = Error.Validation(
        "file_read.too_large",
        "The repository file exceeds the safe read limit.");

    public static readonly Error Failed = Error.Failure(
        "file_read.failed",
        "The repository file could not be read.");
}
