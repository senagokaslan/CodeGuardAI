using System.Security;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Repositories;
using Microsoft.Extensions.Options;

namespace CodeGuardAI.Infrastructure.Repositories;

public sealed class RepositoryScanner : IRepositoryScanner
{
    private readonly ScanOptions _options;
    private readonly SafePathResolver _safePathResolver;

    public RepositoryScanner(IOptions<ScanOptions> options)
        : this(options, new SafePathResolver())
    {
    }

    public RepositoryScanner(IOptions<ScanOptions> options, SafePathResolver safePathResolver)
    {
        _options = options.Value;
        _safePathResolver = safePathResolver;
    }

    public Task<Result<ScanManifest>> ScanAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        if (!HasValidLimits(_options))
        {
            return Task.FromResult(Result.Failure<ScanManifest>(RepositoryScanErrors.InvalidPolicy));
        }

        var rootResolution = _safePathResolver.ResolveRoot(repositoryRoot);
        if (!rootResolution.IsSuccess)
        {
            return Task.FromResult(Result.Failure<ScanManifest>(RepositoryScanErrors.InvalidRoot));
        }

        var resolvedRoot = rootResolution.FullPath!;

        cancellationToken.ThrowIfCancellationRequested();
        var candidates = new List<ScanCandidate>();
        if (!CollectCandidates(resolvedRoot, candidates, _safePathResolver, cancellationToken))
        {
            return Task.FromResult(Result.Failure<ScanManifest>(RepositoryScanErrors.InvalidRoot));
        }

        var orderedCandidates = candidates
            .OrderBy(candidate => candidate.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var entries = ApplyLimits(orderedCandidates, _options, cancellationToken);
        var manifest = new ScanManifest(
            entries,
            entries.Count(entry => entry.IsIncluded),
            entries.Where(entry => entry.IsIncluded).Sum(entry => entry.SizeBytes));

        return Task.FromResult(Result.Success(manifest));
    }

    private static bool CollectCandidates(
        string repositoryRoot,
        ICollection<ScanCandidate> candidates,
        SafePathResolver safePathResolver,
        CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(repositoryRoot);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = directories.Pop();
            string[] paths;

            try
            {
                paths = Directory.GetFileSystemEntries(currentDirectory);
            }
            catch (Exception exception) when (IsAccessException(exception))
            {
                if (string.Equals(currentDirectory, repositoryRoot, StringComparison.Ordinal))
                {
                    return false;
                }

                candidates.Add(new ScanCandidate(
                    ToRelativePath(repositoryRoot, currentDirectory),
                    0,
                    RepositoryLanguage.Unknown,
                    ScanEntryKind.Directory,
                    ScanSkipReason.Inaccessible));
                continue;
            }

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CollectPath(repositoryRoot, path, directories, candidates, safePathResolver);
            }
        }

        return true;
    }

    private static void CollectPath(
        string repositoryRoot,
        string path,
        Stack<string> directories,
        ICollection<ScanCandidate> candidates,
        SafePathResolver safePathResolver)
    {
        var relativePath = ToRelativePath(repositoryRoot, path);
        var resolution = safePathResolver.ResolvePath(repositoryRoot, path);
        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (IsAccessException(exception))
        {
            candidates.Add(new ScanCandidate(
                relativePath,
                0,
                RepositoryLanguage.Unknown,
                ScanEntryKind.File,
                ScanSkipReason.Inaccessible));
            return;
        }

        var isDirectory = attributes.HasFlag(FileAttributes.Directory);
        var kind = isDirectory ? ScanEntryKind.Directory : ScanEntryKind.File;
        if (!resolution.IsSuccess)
        {
            candidates.Add(new ScanCandidate(
                relativePath,
                0,
                RepositoryLanguage.Unknown,
                kind,
                resolution.Failure == SafePathFailure.ReparsePoint
                    ? ScanSkipReason.ReparsePoint
                    : ScanSkipReason.Inaccessible));
            return;
        }

        if (isDirectory)
        {
            if (RepositoryScanPolicy.DeniedDirectoryNames.Contains(Path.GetFileName(path)))
            {
                candidates.Add(new ScanCandidate(
                    relativePath,
                    0,
                    RepositoryLanguage.Unknown,
                    ScanEntryKind.Directory,
                    ScanSkipReason.DirectoryDenied));
                return;
            }

            directories.Push(path);
            return;
        }

        CollectFile(path, relativePath, candidates);
    }

    private static void CollectFile(
        string path,
        string relativePath,
        ICollection<ScanCandidate> candidates)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        var language = GetLanguage(extension);
        long sizeBytes;

        try
        {
            sizeBytes = new FileInfo(path).Length;
        }
        catch (Exception exception) when (IsAccessException(exception))
        {
            candidates.Add(new ScanCandidate(
                relativePath,
                0,
                language,
                ScanEntryKind.File,
                ScanSkipReason.Inaccessible));
            return;
        }

        var skipReason = SecretPathPolicy.IsDenied(fileName)
            ? ScanSkipReason.SensitivePath
            : GetPolicySkipReason(fileName, extension);
        candidates.Add(new ScanCandidate(
            relativePath,
            sizeBytes,
            language,
            ScanEntryKind.File,
            skipReason));
    }

    private static IReadOnlyList<ScanManifestEntry> ApplyLimits(
        IEnumerable<ScanCandidate> candidates,
        ScanOptions options,
        CancellationToken cancellationToken)
    {
        var entries = new List<ScanManifestEntry>();
        var includedFileCount = 0;
        long includedBytes = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var skipReason = candidate.SkipReason;

            if (candidate.Kind == ScanEntryKind.File && skipReason == ScanSkipReason.None)
            {
                if (candidate.SizeBytes > options.MaxFileBytes)
                {
                    skipReason = ScanSkipReason.FileTooLarge;
                }
                else if (includedFileCount >= options.MaxFileCount)
                {
                    skipReason = ScanSkipReason.FileCountLimit;
                }
                else if (candidate.SizeBytes > options.MaxTotalBytes - includedBytes)
                {
                    skipReason = ScanSkipReason.TotalBytesLimit;
                }
                else
                {
                    includedFileCount++;
                    includedBytes += candidate.SizeBytes;
                }
            }

            entries.Add(new ScanManifestEntry(
                candidate.RelativePath,
                candidate.SizeBytes,
                candidate.Language,
                candidate.Kind,
                skipReason));
        }

        return entries;
    }

    private static ScanSkipReason GetPolicySkipReason(string fileName, string extension)
    {
        if (!RepositoryScanPolicy.AllowedExtensions.Contains(extension))
        {
            return ScanSkipReason.ExtensionNotAllowed;
        }

        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
               !RepositoryScanPolicy.AllowedJsonFileNames.Contains(fileName)
            ? ScanSkipReason.JsonFileNotSelected
            : ScanSkipReason.None;
    }

    private static RepositoryLanguage GetLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => RepositoryLanguage.CSharp,
            ".csproj" => RepositoryLanguage.MsBuild,
            ".json" => RepositoryLanguage.Json,
            _ => RepositoryLanguage.Unknown
        };
    }

    private static bool HasValidLimits(ScanOptions options)
    {
        return options.MaxFileCount > 0 &&
               options.MaxFileBytes > 0 &&
               options.MaxTotalBytes > 0;
    }

    private static bool IsAccessException(Exception exception)
    {
        return exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException or SecurityException;
    }

    private static string ToRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private sealed record ScanCandidate(
        string RelativePath,
        long SizeBytes,
        RepositoryLanguage Language,
        ScanEntryKind Kind,
        ScanSkipReason SkipReason);
}
