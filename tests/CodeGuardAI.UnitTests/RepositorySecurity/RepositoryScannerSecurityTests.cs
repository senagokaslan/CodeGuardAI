using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Infrastructure.Repositories;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodeGuardAI.UnitTests.RepositorySecurity;

public sealed class RepositoryScannerSecurityTests : IDisposable
{
    private readonly List<string> _directoryLinks = [];
    private readonly string _fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        "CodeGuardAI.RepositorySecurityScanner",
        Guid.NewGuid().ToString("N"));

    public RepositoryScannerSecurityTests()
    {
        Directory.CreateDirectory(RepositoryRoot);
    }

    private string RepositoryRoot => Path.Combine(_fixtureRoot, "repo");

    [Fact]
    public async Task Scan_skips_secret_files_without_reading_their_content()
    {
        var secretPaths = new[]
        {
            ".env",
            "private.key",
            "certificate.pem",
            "api-key.cs",
            "service-secrets.cs",
            "cloud-credential.cs"
        };
        foreach (var secretPath in secretPaths)
        {
            File.WriteAllText(Path.Combine(RepositoryRoot, secretPath), "DO_NOT_EXPOSE");
        }

        var result = await CreateScanner().ScanAsync(RepositoryRoot, CancellationToken.None);

        Assert.True(result.IsSuccess);
        foreach (var secretPath in secretPaths)
        {
            var entry = Assert.Single(result.Value.Entries, item => item.RelativePath == secretPath);
            Assert.Equal(ScanSkipReason.SensitivePath, entry.SkipReason);
            Assert.False(entry.IsIncluded);
        }
    }

    [Fact]
    public async Task Scan_records_reparse_points_but_does_not_follow_them()
    {
        var outsideDirectory = Path.Combine(_fixtureRoot, "outside");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(Path.Combine(outsideDirectory, "outside.cs"), "class Outside {}");
        var directoryLink = Path.Combine(RepositoryRoot, "linked-directory");
        ReparsePointFixture.CreateDirectoryLink(directoryLink, outsideDirectory);
        _directoryLinks.Add(directoryLink);

        var result = await CreateScanner().ScanAsync(RepositoryRoot, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var linkEntry = Assert.Single(
            result.Value.Entries,
            entry => entry.RelativePath == "linked-directory");
        Assert.Equal(ScanEntryKind.Directory, linkEntry.Kind);
        Assert.Equal(ScanSkipReason.ReparsePoint, linkEntry.SkipReason);
        Assert.DoesNotContain(
            result.Value.Entries,
            entry => entry.RelativePath.StartsWith("linked-directory/", StringComparison.Ordinal));
    }

    private static RepositoryScanner CreateScanner()
    {
        return new RepositoryScanner(Options.Create(new ScanOptions()), new SafePathResolver());
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
