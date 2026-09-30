using System.Security;

namespace CodeGuardAI.Infrastructure.Repositories;

public enum SafePathFailure
{
    None = 0,
    InvalidPath = 1,
    OutsideRoot = 2,
    NotFound = 3,
    ReparsePoint = 4,
    Inaccessible = 5
}

public readonly record struct SafePathResolution(string? FullPath, SafePathFailure Failure)
{
    public bool IsSuccess => Failure == SafePathFailure.None;
}

public sealed class SafePathResolver
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public SafePathResolution ResolveRoot(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return Failure(SafePathFailure.InvalidPath);
        }

        try
        {
            var fullRoot = Normalize(repositoryRoot.Trim());
            var attributes = File.GetAttributes(fullRoot);
            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                return Failure(SafePathFailure.NotFound);
            }

            return attributes.HasFlag(FileAttributes.ReparsePoint)
                ? Failure(SafePathFailure.ReparsePoint)
                : Success(fullRoot);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return Failure(MapFailure(exception));
        }
    }

    public SafePathResolution ResolvePath(string repositoryRoot, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || string.IsNullOrWhiteSpace(targetPath))
        {
            return Failure(SafePathFailure.InvalidPath);
        }

        try
        {
            var fullRoot = Normalize(repositoryRoot);
            var fullTarget = Normalize(Path.IsPathFullyQualified(targetPath)
                ? targetPath
                : Path.Combine(fullRoot, targetPath));

            if (!IsWithinRoot(fullRoot, fullTarget))
            {
                return Failure(SafePathFailure.OutsideRoot);
            }

            var rootAttributes = File.GetAttributes(fullRoot);
            if (!rootAttributes.HasFlag(FileAttributes.Directory))
            {
                return Failure(SafePathFailure.NotFound);
            }

            if (rootAttributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return Failure(SafePathFailure.ReparsePoint);
            }

            return HasReparsePoint(fullRoot, fullTarget)
                ? Failure(SafePathFailure.ReparsePoint)
                : Success(fullTarget);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return Failure(MapFailure(exception));
        }
    }

    private static bool IsWithinRoot(string fullRoot, string fullTarget)
    {
        if (string.Equals(fullRoot, fullTarget, PathComparison))
        {
            return true;
        }

        var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullTarget.StartsWith(rootWithSeparator, PathComparison);
    }

    private static bool HasReparsePoint(string fullRoot, string fullTarget)
    {
        if (File.GetAttributes(fullRoot).HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        var relativePath = Path.GetRelativePath(fullRoot, fullTarget);
        if (relativePath == ".")
        {
            return false;
        }

        var currentPath = fullRoot;
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (File.GetAttributes(currentPath).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static SafePathResolution Success(string fullPath)
    {
        return new SafePathResolution(fullPath, SafePathFailure.None);
    }

    private static SafePathResolution Failure(SafePathFailure failure)
    {
        return new SafePathResolution(null, failure);
    }

    private static SafePathFailure MapFailure(Exception exception)
    {
        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return SafePathFailure.NotFound;
        }

        return exception is UnauthorizedAccessException or SecurityException
            ? SafePathFailure.Inaccessible
            : SafePathFailure.InvalidPath;
    }

    private static bool IsPathException(Exception exception)
    {
        return exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException or SecurityException;
    }
}
