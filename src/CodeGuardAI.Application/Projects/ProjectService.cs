using CodeGuardAI.Application.Common;
using CodeGuardAI.Domain.Projects;

namespace CodeGuardAI.Application.Projects;

public sealed class ProjectService(
    IProjectWriter writer,
    IProjectQueries queries,
    TimeProvider timeProvider) : IProjectService
{
    public async Task<Result<ProjectReadModel>> CreateAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure<ProjectReadModel>(ProjectErrors.InvalidName);
        }

        var name = command.Name.Trim();
        if (name.Length > ProjectLimits.NameMaxLength)
        {
            return Result.Failure<ProjectReadModel>(ProjectErrors.InvalidName);
        }

        if (!TryNormalizePath(command.RepositoryPath, out var repositoryPath, out var normalizedRootPath))
        {
            return Result.Failure<ProjectReadModel>(ProjectErrors.InvalidRepositoryPath);
        }

        var project = Project.Create(
            Guid.NewGuid(),
            name,
            repositoryPath,
            normalizedRootPath,
            timeProvider.GetUtcNow());
        var outcome = await writer.CreateAsync(project, cancellationToken);

        return outcome switch
        {
            ProjectWriteOutcome.Created => Result.Success(ToReadModel(project)),
            ProjectWriteOutcome.DuplicateNormalizedRootPath =>
                Result.Failure<ProjectReadModel>(ProjectErrors.DuplicateRepositoryPath),
            _ => throw new InvalidOperationException($"Unsupported project write outcome: {outcome}.")
        };
    }

    public async Task<Result<ProjectReadModel>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<ProjectReadModel>(ProjectErrors.NotFound);
        }

        var project = await queries.GetByIdAsync(id, cancellationToken);
        return project is null
            ? Result.Failure<ProjectReadModel>(ProjectErrors.NotFound)
            : Result.Success(project);
    }

    public async Task<Result<ProjectPage>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var skip = ((long)page - 1) * pageSize;
        if (page < 1 || pageSize is < 1 or > ProjectLimits.MaxPageSize || skip > int.MaxValue)
        {
            return Result.Failure<ProjectPage>(ProjectErrors.InvalidPaging);
        }

        var projects = await queries.ListAsync((int)skip, pageSize, cancellationToken);
        return Result.Success(new ProjectPage(projects, page, pageSize));
    }

    private static bool TryNormalizePath(
        string? repositoryPath,
        out string canonicalPath,
        out string normalizedRootPath)
    {
        canonicalPath = string.Empty;
        normalizedRootPath = string.Empty;

        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return false;
        }

        try
        {
            canonicalPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(repositoryPath.Trim()));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (canonicalPath.Length is 0 or > ProjectLimits.RepositoryPathMaxLength)
        {
            return false;
        }

        normalizedRootPath = OperatingSystem.IsWindows()
            ? canonicalPath.ToUpperInvariant()
            : canonicalPath;
        return true;
    }

    private static ProjectReadModel ToReadModel(Project project)
    {
        return new ProjectReadModel(
            project.Id,
            project.Name,
            project.RepositoryPath,
            project.CreatedAtUtc);
    }
}
