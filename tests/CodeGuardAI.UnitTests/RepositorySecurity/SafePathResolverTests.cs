using CodeGuardAI.Infrastructure.Repositories;
using Xunit;

namespace CodeGuardAI.UnitTests.RepositorySecurity;

public sealed class SafePathResolverTests : IDisposable
{
    private readonly List<string> _directoryLinks = [];
    private readonly string _fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        "CodeGuardAI.RepositorySecurity",
        Guid.NewGuid().ToString("N"));

    public SafePathResolverTests()
    {
        Directory.CreateDirectory(RepositoryRoot);
    }

    private string RepositoryRoot => Path.Combine(_fixtureRoot, "repo");

    [Fact]
    public void ResolvePath_rejects_parent_traversal()
    {
        var outsideFile = Path.Combine(_fixtureRoot, "outside.cs");
        File.WriteAllText(outsideFile, "class Outside {}");
        var resolver = new SafePathResolver();

        var result = resolver.ResolvePath(RepositoryRoot, Path.Combine("..", "outside.cs"));

        Assert.False(result.IsSuccess);
        Assert.Null(result.FullPath);
        Assert.Equal(SafePathFailure.OutsideRoot, result.Failure);
    }

    [Fact]
    public void ResolvePath_rejects_sibling_prefix_bypass()
    {
        var siblingRoot = Path.Combine(_fixtureRoot, "repo-sensitive");
        Directory.CreateDirectory(siblingRoot);
        var siblingFile = Path.Combine(siblingRoot, "source.cs");
        File.WriteAllText(siblingFile, "class Sibling {}");
        var resolver = new SafePathResolver();

        var result = resolver.ResolvePath(RepositoryRoot, siblingFile);

        Assert.False(result.IsSuccess);
        Assert.Equal(SafePathFailure.OutsideRoot, result.Failure);
    }

    [Fact]
    public void ResolvePath_rejects_reparse_directory_and_files_reached_through_it()
    {
        var outsideDirectory = Path.Combine(_fixtureRoot, "outside");
        Directory.CreateDirectory(outsideDirectory);
        var outsideFile = Path.Combine(outsideDirectory, "outside.cs");
        File.WriteAllText(outsideFile, "class Outside {}");

        var directoryLink = Path.Combine(RepositoryRoot, "linked-directory");
        ReparsePointFixture.CreateDirectoryLink(directoryLink, outsideDirectory);
        _directoryLinks.Add(directoryLink);
        var resolver = new SafePathResolver();

        var directoryResult = resolver.ResolvePath(RepositoryRoot, directoryLink);
        var nestedResult = resolver.ResolvePath(RepositoryRoot, Path.Combine(directoryLink, "outside.cs"));

        Assert.Equal(SafePathFailure.ReparsePoint, directoryResult.Failure);
        Assert.Equal(SafePathFailure.ReparsePoint, nestedResult.Failure);
    }

    [Fact]
    public void ResolveRoot_returns_only_canonical_existing_directory()
    {
        var resolver = new SafePathResolver();
        var nonCanonicalRoot = Path.Combine(RepositoryRoot, ".", "nested", "..");

        var result = resolver.ResolveRoot(nonCanonicalRoot);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(RepositoryRoot)),
            result.FullPath,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    public void Dispose()
    {
        foreach (var directoryLink in _directoryLinks)
        {
            ReparsePointFixture.DeleteDirectoryLink(directoryLink);
        }

        if (Directory.Exists(_fixtureRoot))
        {
            Directory.Delete(_fixtureRoot, recursive: true);
        }
    }
}
