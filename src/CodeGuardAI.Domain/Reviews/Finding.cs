using System.IO;
using CodeGuardAI.Domain.Common;

namespace CodeGuardAI.Domain.Reviews;

public sealed class Finding
{
    private Finding(
        Guid id,
        Guid reviewRunId,
        FindingSeverity severity,
        FindingCategory category,
        string filePath,
        int startLine,
        int endLine,
        string title,
        string reason,
        string suggestion,
        decimal confidence,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ReviewRunId = reviewRunId;
        Severity = severity;
        Category = category;
        FilePath = filePath;
        StartLine = startLine;
        EndLine = endLine;
        Title = title;
        Reason = reason;
        Suggestion = suggestion;
        Confidence = confidence;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }
    public Guid ReviewRunId { get; }
    public FindingSeverity Severity { get; }
    public FindingCategory Category { get; }
    public string FilePath { get; }
    public int StartLine { get; }
    public int EndLine { get; }
    public string Title { get; }
    public string Reason { get; }
    public string Suggestion { get; }
    public decimal Confidence { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public static Finding Create(
        Guid id,
        Guid reviewRunId,
        FindingSeverity severity,
        FindingCategory category,
        string filePath,
        int startLine,
        int endLine,
        string title,
        string reason,
        string suggestion,
        decimal confidence,
        DateTimeOffset createdAtUtc)
    {
        var validatedPath = DomainGuard.Required(filePath, nameof(filePath));
        if (Path.IsPathRooted(validatedPath))
        {
            throw new ArgumentException("The finding file path must be repository-relative.", nameof(filePath));
        }

        if (startLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine), startLine, "The start line must be at least 1.");
        }

        if (endLine < startLine)
        {
            throw new ArgumentOutOfRangeException(nameof(endLine), endLine, "The end line cannot precede the start line.");
        }

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be between 0 and 1.");
        }

        return new Finding(
            DomainGuard.NotEmpty(id, nameof(id)),
            DomainGuard.NotEmpty(reviewRunId, nameof(reviewRunId)),
            DomainGuard.DefinedEnum(severity, nameof(severity)),
            DomainGuard.DefinedEnum(category, nameof(category)),
            validatedPath,
            startLine,
            endLine,
            DomainGuard.Required(title, nameof(title)),
            DomainGuard.Required(reason, nameof(reason)),
            DomainGuard.Required(suggestion, nameof(suggestion)),
            confidence,
            DomainGuard.Utc(createdAtUtc, nameof(createdAtUtc)));
    }
}
