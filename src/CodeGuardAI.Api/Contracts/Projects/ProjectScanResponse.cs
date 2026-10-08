namespace CodeGuardAI.Api.Contracts.Projects;

public sealed record ProjectScanEntryResponse(
    string RelativePath,
    long SizeBytes,
    string Language,
    string Kind,
    string SkipReason,
    bool IsIncluded);

public sealed record ProjectScanResponse(
    Guid ProjectId,
    int IncludedFileCount,
    int SkippedEntryCount,
    long IncludedBytes,
    IReadOnlyList<ProjectScanEntryResponse> Entries);
