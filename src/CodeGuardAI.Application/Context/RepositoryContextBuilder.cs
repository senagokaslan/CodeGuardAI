using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Application.Tools;

namespace CodeGuardAI.Application.Context;

public sealed class RepositoryContextBuilder(
    IFileReadTool fileReadTool,
    RepositoryContextOptions options) : IRepositoryContextBuilder
{
    public async Task<Result<RepositoryContext>> BuildAsync(
        string repositoryRoot,
        ScanManifest manifest,
        CancellationToken cancellationToken)
    {
        if (options.MaxCharacters < RepositoryContext.TruncationMarker.Length ||
            options.MaxCharactersPerFile < RepositoryContext.TruncationMarker.Length)
        {
            return Result.Failure<RepositoryContext>(RepositoryContextErrors.InvalidPolicy);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var segments = new List<RepositoryContextSegment>();
        var failures = new List<RepositoryContextReadFailure>();
        var characterCount = 0;

        foreach (var entry in manifest.Entries
                     .Where(candidate => candidate.IsIncluded)
                     .OrderBy(GetPriority)
                     .ThenBy(candidate => candidate.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.MaxCharacters - characterCount < RepositoryContext.TruncationMarker.Length)
            {
                break;
            }

            var readResult = await fileReadTool.ExecuteAsync(
                new FileReadToolInput(repositoryRoot, entry.RelativePath),
                cancellationToken);
            if (readResult.IsFailure)
            {
                failures.Add(new RepositoryContextReadFailure(
                    entry.RelativePath,
                    readResult.ErrorCode!));
                continue;
            }

            var availableCharacters = Math.Min(
                options.MaxCharactersPerFile,
                options.MaxCharacters - characterCount);
            characterCount += AddFileSegments(
                readResult.Value,
                availableCharacters,
                segments);
        }

        return Result.Success(new RepositoryContext(segments, failures, characterCount));
    }

    private static int AddFileSegments(
        FileReadContent file,
        int availableCharacters,
        ICollection<RepositoryContextSegment> segments)
    {
        var lines = ReadLines(file.Content);
        var totalCharacters = lines.Sum(line => line.Length);
        var isTruncated = totalCharacters > availableCharacters;
        var markerLength = RepositoryContext.TruncationMarker.Length;
        var contentBudget = isTruncated
            ? Math.Max(0, availableCharacters - markerLength)
            : availableCharacters;
        var addedCharacters = 0;
        var nextLine = 1;

        for (var index = 0; index < lines.Count && addedCharacters < contentBudget; index++)
        {
            var line = lines[index];
            var remaining = contentBudget - addedCharacters;
            var includedContent = line.Length <= remaining ? line : line[..remaining];
            if (includedContent.Length > 0)
            {
                var lineNumber = index + 1;
                segments.Add(new RepositoryContextSegment(
                    file.RelativePath,
                    lineNumber,
                    lineNumber,
                    includedContent));
                addedCharacters += includedContent.Length;
            }

            nextLine = index + 2;
            if (includedContent.Length < line.Length)
            {
                nextLine = index + 1;
                break;
            }
        }

        if (isTruncated && availableCharacters - addedCharacters >= markerLength)
        {
            segments.Add(new RepositoryContextSegment(
                file.RelativePath,
                Math.Min(nextLine, Math.Max(1, lines.Count)),
                Math.Min(nextLine, Math.Max(1, lines.Count)),
                RepositoryContext.TruncationMarker,
                IsTruncationMarker: true));
            addedCharacters += markerLength;
        }

        return addedCharacters;
    }

    private static IReadOnlyList<string> ReadLines(string content)
    {
        var lines = new List<string>();
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static int GetPriority(ScanManifestEntry entry)
    {
        return Path.GetExtension(entry.RelativePath).ToLowerInvariant() switch
        {
            ".cs" => 0,
            ".csproj" => 1,
            ".json" => 2,
            _ => 3
        };
    }
}
