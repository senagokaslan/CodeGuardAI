namespace CodeGuardAI.Application.Repositories;

public enum ScanEntryKind
{
    File = 1,
    Directory = 2
}

public enum RepositoryLanguage
{
    Unknown = 0,
    CSharp = 1,
    MsBuild = 2,
    Json = 3
}

public enum ScanSkipReason
{
    None = 0,
    DirectoryDenied = 1,
    SensitivePath = 2,
    ExtensionNotAllowed = 3,
    JsonFileNotSelected = 4,
    FileTooLarge = 5,
    TotalBytesLimit = 6,
    FileCountLimit = 7,
    ReparsePoint = 8,
    Inaccessible = 9
}

public sealed record ScanManifestEntry(
    string RelativePath,
    long SizeBytes,
    RepositoryLanguage Language,
    ScanEntryKind Kind,
    ScanSkipReason SkipReason)
{
    public bool IsIncluded => Kind == ScanEntryKind.File && SkipReason == ScanSkipReason.None;
}

public sealed record ScanManifest(
    IReadOnlyList<ScanManifestEntry> Entries,
    int IncludedFileCount,
    long IncludedBytes);
