using System.Text.Json.Serialization;
using CodeGuardAI.Domain.Reviews;

namespace CodeGuardAI.Application.Reviews.Models;

public sealed record LLMFinding
{
    [JsonConstructor]
    public LLMFinding(
        FindingSeverity severity,
        FindingCategory category,
        string filePath,
        int startLine,
        int endLine,
        string title,
        string reason,
        string suggestion,
        decimal confidence)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestion);
        var normalizedPath = filePath.Trim();
        if (Path.IsPathRooted(normalizedPath) || ContainsParentTraversal(normalizedPath))
        {
            throw new ArgumentException(
                "Finding paths must be repository-relative and cannot contain parent traversal.",
                nameof(filePath));
        }

        if (startLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine), "Start line must be at least 1.");
        }

        if (endLine < startLine)
        {
            throw new ArgumentOutOfRangeException(nameof(endLine), "End line cannot precede start line.");
        }

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.");
        }

        Severity = severity;
        Category = category;
        FilePath = normalizedPath;
        StartLine = startLine;
        EndLine = endLine;
        Title = title.Trim();
        Reason = reason.Trim();
        Suggestion = suggestion.Trim();
        Confidence = confidence;
    }

    public FindingSeverity Severity { get; }

    public FindingCategory Category { get; }

    public string FilePath { get; }

    public int StartLine { get; }

    public int EndLine { get; }

    public string Title { get; }

    public string Reason { get; }

    public string Suggestion { get; }

    public decimal Confidence { get; }

    private static bool ContainsParentTraversal(string path)
    {
        return path.Split(
                ['/', '\\'],
                StringSplitOptions.RemoveEmptyEntries)
            .Contains("..", StringComparer.Ordinal);
    }
}
