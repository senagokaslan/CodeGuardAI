using CodeGuardAI.Application.Common;

namespace CodeGuardAI.Application.Projects;

public interface IProjectService
{
    Task<Result<ProjectReadModel>> CreateAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken);

    Task<Result<ProjectReadModel>> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<ProjectPage>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
}
