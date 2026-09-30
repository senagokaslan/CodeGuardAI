using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Domain.Projects;
using Xunit;

namespace CodeGuardAI.UnitTests.Projects;

public sealed class ProjectServiceTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_normalizes_input_before_writing_project()
    {
        var writer = new StubWriter(ProjectWriteOutcome.Created);
        var service = CreateService(writer: writer);
        var inputPath = Path.Combine(Path.GetTempPath(), "CodeGuard", ".") + Path.DirectorySeparatorChar;

        var result = await service.CreateAsync(
            new CreateProjectCommand("  CodeGuard AI  ", inputPath),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var written = Assert.IsType<Project>(writer.WrittenProject);
        var expectedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputPath.Trim()));
        Assert.Equal("CodeGuard AI", written.Name);
        Assert.Equal(expectedPath, written.RepositoryPath);
        Assert.Equal(
            OperatingSystem.IsWindows() ? expectedPath.ToUpperInvariant() : expectedPath,
            written.NormalizedRootPath);
        Assert.Equal(UtcNow, written.CreatedAtUtc);
        Assert.Equal(expectedPath, result.Value.RepositoryPath);
    }

    [Fact]
    public async Task Create_when_normalized_path_is_duplicate_returns_conflict()
    {
        var writer = new StubWriter(ProjectWriteOutcome.DuplicateNormalizedRootPath);
        var service = CreateService(writer: writer);

        var result = await service.CreateAsync(
            new CreateProjectCommand("CodeGuard AI", Path.GetTempPath()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("project.repository_path_conflict", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_with_blank_name_returns_validation_without_writing(string name)
    {
        var writer = new StubWriter(ProjectWriteOutcome.Created);
        var service = CreateService(writer: writer);

        var result = await service.CreateAsync(
            new CreateProjectCommand(name, Path.GetTempPath()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Null(writer.WrittenProject);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, ProjectLimits.MaxPageSize + 1)]
    [InlineData(int.MaxValue, ProjectLimits.MaxPageSize)]
    public async Task List_with_unbounded_paging_returns_validation_without_querying(int page, int pageSize)
    {
        var queries = new StubQueries();
        var service = CreateService(queries: queries);

        var result = await service.ListAsync(page, pageSize, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(0, queries.ListCallCount);
    }

    [Fact]
    public async Task List_uses_bounded_skip_and_take()
    {
        var queries = new StubQueries();
        var service = CreateService(queries: queries);

        var result = await service.ListAsync(3, 25, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(50, queries.LastSkip);
        Assert.Equal(25, queries.LastTake);
        Assert.Equal(3, result.Value.Page);
        Assert.Equal(25, result.Value.PageSize);
    }

    [Fact]
    public async Task Get_missing_project_returns_not_found()
    {
        var service = CreateService();

        var result = await service.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("project.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Create_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var writer = new StubWriter(ProjectWriteOutcome.Created, observeCancellation: true);
        var service = CreateService(writer: writer);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(
            new CreateProjectCommand("CodeGuard AI", Path.GetTempPath()),
            cancellation.Token));
    }

    private static ProjectService CreateService(
        StubWriter? writer = null,
        StubQueries? queries = null)
    {
        return new ProjectService(
            writer ?? new StubWriter(ProjectWriteOutcome.Created),
            queries ?? new StubQueries(),
            new FixedTimeProvider(UtcNow));
    }

    private sealed class StubWriter(
        ProjectWriteOutcome outcome,
        bool observeCancellation = false) : IProjectWriter
    {
        public Project? WrittenProject { get; private set; }

        public Task<ProjectWriteOutcome> CreateAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            if (observeCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            WrittenProject = project;
            return Task.FromResult(outcome);
        }
    }

    private sealed class StubQueries : IProjectQueries
    {
        public int ListCallCount { get; private set; }
        public int LastSkip { get; private set; }
        public int LastTake { get; private set; }

        public Task<ProjectReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult<ProjectReadModel?>(null);
        }

        public Task<IReadOnlyList<ProjectReadModel>> ListAsync(
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            ListCallCount++;
            LastSkip = skip;
            LastTake = take;
            return Task.FromResult<IReadOnlyList<ProjectReadModel>>([]);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
