using CodeGuardAI.Domain.Common;

namespace CodeGuardAI.Domain.Documentation;

public sealed class DocumentationReport
{
    public const int MaxTitleLength = 200;
    public const int MaxMarkdownContentLength = 200_000;

    private DocumentationReport(
        Guid id,
        Guid reviewRunId,
        Guid modelRunId,
        string title,
        string markdownContent,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ReviewRunId = reviewRunId;
        ModelRunId = modelRunId;
        Title = title;
        MarkdownContent = markdownContent;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid ReviewRunId { get; }

    public Guid ModelRunId { get; }

    public string Title { get; }

    public string MarkdownContent { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static DocumentationReport Create(
        Guid id,
        Guid reviewRunId,
        Guid modelRunId,
        string title,
        string markdownContent,
        DateTimeOffset createdAtUtc)
    {
        return new DocumentationReport(
            DomainGuard.NotEmpty(id, nameof(id)),
            DomainGuard.NotEmpty(reviewRunId, nameof(reviewRunId)),
            DomainGuard.NotEmpty(modelRunId, nameof(modelRunId)),
            RequiredBounded(title, MaxTitleLength, nameof(title)),
            RequiredBounded(markdownContent, MaxMarkdownContentLength, nameof(markdownContent)),
            DomainGuard.Utc(createdAtUtc, nameof(createdAtUtc)));
    }

    private static string RequiredBounded(string? value, int maxLength, string parameterName)
    {
        var normalized = DomainGuard.Required(value, parameterName);
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                normalized.Length,
                $"The value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}
