using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Infrastructure.Repositories;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodeGuardAI.UnitTests.Repositories;

public sealed class RepositoryScannerTests
{
    [Fact]
    public async Task Scan_applies_allowlist_denylist_and_sensitive_name_policy()
    {
        using var repository = new TemporaryRepository();
        repository.WriteFile("z.cs", "class Z {}");
        repository.WriteFile("a.csproj", "<Project />");
        repository.WriteFile("global.json", "{}");
        repository.WriteFile("package.json", "{}");
        repository.WriteFile("notes.txt", "not source");
        repository.WriteFile(".env", "SECRET=value");
        repository.WriteFile("my-secret.cs", "do not include");
        foreach (var directory in new[] { ".git", "bin", "obj", "node_modules" })
        {
            repository.WriteFile($"{directory}/ignored.cs", "class Ignored {}");
        }

        var result = await CreateScanner().ScanAsync(repository.Root, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            result.Value.Entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal),
            result.Value.Entries);
        Assert.Equal(
            ["a.csproj", "global.json", "z.cs"],
            result.Value.Entries.Where(entry => entry.IsIncluded).Select(entry => entry.RelativePath));
        AssertEntry(result.Value, ".env", ScanSkipReason.SensitivePath);
        AssertEntry(result.Value, "my-secret.cs", ScanSkipReason.SensitivePath);
        AssertEntry(result.Value, "notes.txt", ScanSkipReason.ExtensionNotAllowed);
        AssertEntry(result.Value, "package.json", ScanSkipReason.JsonFileNotSelected);

        foreach (var directory in new[] { ".git", "bin", "obj", "node_modules" })
        {
            var entry = AssertEntry(result.Value, directory, ScanSkipReason.DirectoryDenied);
            Assert.Equal(ScanEntryKind.Directory, entry.Kind);
            Assert.DoesNotContain(result.Value.Entries, candidate =>
                candidate.RelativePath.StartsWith($"{directory}/", StringComparison.Ordinal));
        }

        Assert.Equal(RepositoryLanguage.CSharp, FindEntry(result.Value, "z.cs").Language);
        Assert.Equal(RepositoryLanguage.MsBuild, FindEntry(result.Value, "a.csproj").Language);
        Assert.Equal(RepositoryLanguage.Json, FindEntry(result.Value, "global.json").Language);
    }

    [Fact]
    public async Task Scan_assigns_deterministic_skip_reasons_for_each_limit()
    {
        using var repository = new TemporaryRepository();
        repository.WriteFile("a.cs", "1234");
        repository.WriteFile("b.cs", "123456");
        repository.WriteFile("c.cs", "1234");
        repository.WriteFile("d.cs", "1");
        repository.WriteFile("e.cs", "1");
        var scanner = CreateScanner(new ScanOptions
        {
            MaxFileCount = 2,
            MaxFileBytes = 5,
            MaxTotalBytes = 5
        });

        var result = await scanner.ScanAsync(repository.Root, CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertEntry(result.Value, "a.cs", ScanSkipReason.None);
        AssertEntry(result.Value, "b.cs", ScanSkipReason.FileTooLarge);
        AssertEntry(result.Value, "c.cs", ScanSkipReason.TotalBytesLimit);
        AssertEntry(result.Value, "d.cs", ScanSkipReason.None);
        AssertEntry(result.Value, "e.cs", ScanSkipReason.FileCountLimit);
        Assert.Equal(2, result.Value.IncludedFileCount);
        Assert.Equal(5, result.Value.IncludedBytes);
    }

    [Fact]
    public async Task Same_repository_and_policy_produce_same_manifest()
    {
        using var repository = new TemporaryRepository();
        repository.WriteFile("nested/B.cs", "class B {}");
        repository.WriteFile("A.cs", "class A {}");
        repository.WriteFile("appsettings.json", "{}");
        var scanner = CreateScanner();

        var first = await scanner.ScanAsync(repository.Root, CancellationToken.None);
        var second = await scanner.ScanAsync(repository.Root, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Entries, second.Value.Entries);
        Assert.Equal(first.Value.IncludedFileCount, second.Value.IncludedFileCount);
        Assert.Equal(first.Value.IncludedBytes, second.Value.IncludedBytes);
    }

    [Fact]
    public async Task Missing_root_returns_typed_validation_failure()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");

        var result = await CreateScanner().ScanAsync(missingRoot, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("repository.root_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Invalid_limits_return_typed_failure()
    {
        var scanner = CreateScanner(new ScanOptions
        {
            MaxFileCount = 0,
            MaxFileBytes = 1,
            MaxTotalBytes = 1
        });

        var result = await scanner.ScanAsync(Path.GetTempPath(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal("repository.scan_policy_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        using var repository = new TemporaryRepository();
        repository.WriteFile("A.cs", "class A {}");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateScanner().ScanAsync(repository.Root, cancellation.Token));
    }

    private static RepositoryScanner CreateScanner(ScanOptions? options = null)
    {
        return new RepositoryScanner(Options.Create(options ?? new ScanOptions()));
    }

    private static ScanManifestEntry AssertEntry(
        ScanManifest manifest,
        string relativePath,
        ScanSkipReason skipReason)
    {
        var entry = FindEntry(manifest, relativePath);
        Assert.Equal(skipReason, entry.SkipReason);
        return entry;
    }

    private static ScanManifestEntry FindEntry(ScanManifest manifest, string relativePath)
    {
        return Assert.Single(manifest.Entries, entry => entry.RelativePath == relativePath);
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            Root = Path.Combine(Path.GetTempPath(), "CodeGuardAI.ScannerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void WriteFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Test file directory could not be resolved.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
