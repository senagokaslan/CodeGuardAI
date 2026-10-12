namespace CodeGuardAI.Api.Contracts.Reviews;

public sealed record DocumentationResponse(
    Guid Id,
    Guid ReviewId,
    Guid ModelRunId,
    string Title,
    string MarkdownContent,
    DateTimeOffset CreatedAtUtc);
