namespace CodeGuardAI.Application.Repositories;

public sealed class ScanOptions
{
    public const string SectionName = "Scan";

    public int MaxFileCount { get; init; } = 500;

    public long MaxFileBytes { get; init; } = 256 * 1024;

    public long MaxTotalBytes { get; init; } = 2 * 1024 * 1024;
}
