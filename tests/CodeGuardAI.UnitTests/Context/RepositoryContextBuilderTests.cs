using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Context;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Application.Tools;
using Xunit;

namespace CodeGuardAI.UnitTests.Context;

public sealed class RepositoryContextBuilderTests
{
    [Fact]
    public async Task Build_reads_only_included_manifest_files_in_explicit_priority_order()
    {
        var reader = new RecordingFileReadTool(new Dictionary<string, Result<FileReadContent>>
        {
            ["source.cs"] = Success("source.cs", "source"),
            ["project.csproj"] = Success("project.csproj", "project"),
            ["appsettings.json"] = Success("appsettings.json", "config")
        });
        var manifest = Manifest(
            Entry("appsettings.json", RepositoryLanguage.Json),
            Entry("ignored.cs", RepositoryLanguage.CSharp, ScanSkipReason.SensitivePath),
            Entry("folder", RepositoryLanguage.Unknown, kind: ScanEntryKind.Directory),
            Entry("project.csproj", RepositoryLanguage.MsBuild),
            Entry("source.cs", RepositoryLanguage.CSharp));

        var result = await CreateBuilder(reader).BuildAsync(
            "repository",
            manifest,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["source.cs", "project.csproj", "appsettings.json"], reader.ReadPaths);
        Assert.DoesNotContain("ignored.cs", reader.ReadPaths);
        Assert.DoesNotContain("folder", reader.ReadPaths);
    }

    [Fact]
    public async Task Build_emits_one_based_line_metadata_for_each_segment()
    {
        var reader = new RecordingFileReadTool(new Dictionary<string, Result<FileReadContent>>
        {
            ["source.cs"] = Success("source.cs", "first\r\nsecond\nthird")
        });

        var result = await CreateBuilder(reader).BuildAsync(
            "repository",
            Manifest(Entry("source.cs", RepositoryLanguage.CSharp)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value.Segments,
            segment => AssertSegment(segment, "source.cs", 1, "first"),
            segment => AssertSegment(segment, "source.cs", 2, "second"),
            segment => AssertSegment(segment, "source.cs", 3, "third"));
    }

    [Fact]
    public async Task Build_never_exceeds_global_or_per_file_budget_and_marks_truncation()
    {
        var reader = new RecordingFileReadTool(new Dictionary<string, Result<FileReadContent>>
        {
            ["a.cs"] = Success("a.cs", "12345678901234567890"),
            ["b.cs"] = Success("b.cs", "abcdefghijabcdefghij")
        });
        var options = new RepositoryContextOptions
        {
            MaxCharacters = 30,
            MaxCharactersPerFile = 15
        };

        var result = await CreateBuilder(reader, options).BuildAsync(
            "repository",
            Manifest(
                Entry("a.cs", RepositoryLanguage.CSharp),
                Entry("b.cs", RepositoryLanguage.CSharp)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Value.CharacterCount);
        Assert.Equal(
            result.Value.CharacterCount,
            result.Value.Segments.Sum(segment => segment.Content.Length));
        Assert.All(
            result.Value.Segments.GroupBy(segment => segment.RelativePath),
            group => Assert.True(group.Sum(segment => segment.Content.Length) <= 15));
        Assert.Equal(
            2,
            result.Value.Segments.Count(segment =>
                segment.IsTruncationMarker &&
                segment.Content == RepositoryContext.TruncationMarker));
    }

    [Fact]
    public async Task Read_failure_is_recorded_and_does_not_block_other_files()
    {
        var reader = new RecordingFileReadTool(new Dictionary<string, Result<FileReadContent>>
        {
            ["a.cs"] = Result.Failure<FileReadContent>(FileReadErrors.InvalidEncoding),
            ["b.cs"] = Success("b.cs", "safe")
        });

        var result = await CreateBuilder(reader).BuildAsync(
            "repository",
            Manifest(
                Entry("a.cs", RepositoryLanguage.CSharp),
                Entry("b.cs", RepositoryLanguage.CSharp)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var failure = Assert.Single(result.Value.ReadFailures);
        Assert.Equal("a.cs", failure.RelativePath);
        Assert.Equal("file_read.invalid_utf8", failure.ErrorCode);
        var segment = Assert.Single(result.Value.Segments);
        Assert.Equal("b.cs", segment.RelativePath);
        Assert.Equal("safe", segment.Content);
    }

    [Fact]
    public async Task Cancellation_is_propagated_before_any_read()
    {
        var reader = new RecordingFileReadTool(
            new Dictionary<string, Result<FileReadContent>>());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateBuilder(reader).BuildAsync(
                "repository",
                Manifest(Entry("source.cs", RepositoryLanguage.CSharp)),
                cancellation.Token));
        Assert.Empty(reader.ReadPaths);
    }

    private static RepositoryContextBuilder CreateBuilder(
        IFileReadTool reader,
        RepositoryContextOptions? options = null)
    {
        return new RepositoryContextBuilder(reader, options ?? new RepositoryContextOptions());
    }

    private static Result<FileReadContent> Success(string relativePath, string content)
    {
        return Result.Success(new FileReadContent(relativePath, content));
    }

    private static ScanManifest Manifest(params ScanManifestEntry[] entries)
    {
        return new ScanManifest(
            entries,
            entries.Count(entry => entry.IsIncluded),
            entries.Where(entry => entry.IsIncluded).Sum(entry => entry.SizeBytes));
    }

    private static ScanManifestEntry Entry(
        string path,
        RepositoryLanguage language,
        ScanSkipReason skipReason = ScanSkipReason.None,
        ScanEntryKind kind = ScanEntryKind.File)
    {
        return new ScanManifestEntry(path, 10, language, kind, skipReason);
    }

    private static void AssertSegment(
        RepositoryContextSegment segment,
        string path,
        int line,
        string content)
    {
        Assert.Equal(path, segment.RelativePath);
        Assert.Equal(line, segment.StartLine);
        Assert.Equal(line, segment.EndLine);
        Assert.Equal(content, segment.Content);
        Assert.False(segment.IsTruncationMarker);
    }

    private sealed class RecordingFileReadTool(
        IReadOnlyDictionary<string, Result<FileReadContent>> results) : IFileReadTool
    {
        public List<string> ReadPaths { get; } = [];

        public Task<ToolResult<FileReadContent>> ExecuteAsync(
            FileReadToolInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadPaths.Add(input.RelativePath);
            var result = results[input.RelativePath];
            return Task.FromResult(result.IsSuccess
                ? ToolResult<FileReadContent>.Success(result.Value, TimeSpan.Zero)
                : ToolResult<FileReadContent>.Failure(result.Error.Code, TimeSpan.Zero));
        }
    }
}
