using CodeGuardAI.Application.Projects;
using CodeGuardAI.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodeGuardAI.Infrastructure.Persistence;

internal sealed class EfProjectWriter(CodeGuardDbContext dbContext) : IProjectWriter
{
    private const string NormalizedRootPathIndex = "ux_projects_normalized_root_path";

    public async Task<ProjectWriteOutcome> CreateAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        dbContext.Projects.Add(project);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ProjectWriteOutcome.Created;
        }
        catch (DbUpdateException exception) when (IsNormalizedPathConflict(exception))
        {
            dbContext.Entry(project).State = EntityState.Detached;
            return ProjectWriteOutcome.DuplicateNormalizedRootPath;
        }
    }

    private static bool IsNormalizedPathConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: NormalizedRootPathIndex
        };
    }
}
