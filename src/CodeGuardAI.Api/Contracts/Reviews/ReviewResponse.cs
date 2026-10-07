namespace CodeGuardAI.Api.Contracts.Reviews;

public sealed record ReviewFindingResponse(
    Guid Id,
    string FilePath,
    int StartLine,
    int EndLine,
    string Severity,
    string Category,
    string Title,
    string Reason,
    string Suggestion,
    decimal Confidence);

public sealed record ReviewScanSummaryResponse(
    int IncludedFileCount,
    int SkippedEntryCount,
    long IncludedBytes);

public sealed record ReviewResponse(
    Guid Id,
    Guid ProjectId,
    string Status,
    string Model,
    string PromptVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorCode,
    ReviewScanSummaryResponse? ScanSummary,
    IReadOnlyList<ReviewFindingResponse> Findings);

public sealed record ReviewHistoryResponse(IReadOnlyList<ReviewResponse> Items);
