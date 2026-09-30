using CodeGuardAI.Application.Common;

namespace CodeGuardAI.Application.Context;

public sealed class RepositoryContextOptions
{
    public const int DefaultMaxCharacters = 32_000;
    public const int DefaultMaxCharactersPerFile = 8_000;

    public int MaxCharacters { get; init; } = DefaultMaxCharacters;

    public int MaxCharactersPerFile { get; init; } = DefaultMaxCharactersPerFile;
}

public sealed record RepositoryContextSegment(
    string RelativePath,
    int StartLine,
    int EndLine,
    string Content,
    bool IsTruncationMarker = false);

public sealed record RepositoryContextReadFailure(
    string RelativePath,
    string ErrorCode);

public sealed record RepositoryContext(
    IReadOnlyList<RepositoryContextSegment> Segments,
    IReadOnlyList<RepositoryContextReadFailure> ReadFailures,
    int CharacterCount)
{
    public const string TruncationMarker = "[TRUNCATED]";
}

public static class RepositoryContextErrors
{
    public static readonly Error InvalidPolicy = Error.Validation(
        "repository_context.policy_invalid",
        "Repository context character limits must be positive.");
}
