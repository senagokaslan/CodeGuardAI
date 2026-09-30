using CodeGuardAI.Application.Projects;
using Microsoft.EntityFrameworkCore;

namespace CodeGuardAI.Infrastructure.Persistence.Queries;

internal sealed class EfProjectQueries(CodeGuardDbContext dbContext) : IProjectQueries
{
    public Task<ProjectReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Projects
            .AsNoTracking()
            .Where(project => project.Id == id)
            .Select(project => new ProjectReadModel(
                project.Id,
                project.Name,
                project.RepositoryPath,
                project.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectReadModel>> ListAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return await dbContext.Projects
            .AsNoTracking()
            .OrderBy(project => project.Name)
            .ThenBy(project => project.Id)
            .Skip(skip)
            .Take(take)
            .Select(project => new ProjectReadModel(
                project.Id,
                project.Name,
                project.RepositoryPath,
                project.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
