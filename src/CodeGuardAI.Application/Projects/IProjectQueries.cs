namespace CodeGuardAI.Application.Projects;

public interface IProjectQueries
{
    Task<ProjectReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectReadModel>> ListAsync(
        int skip,
        int take,
        CancellationToken cancellationToken);
}
