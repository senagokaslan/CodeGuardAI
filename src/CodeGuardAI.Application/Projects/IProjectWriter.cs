using CodeGuardAI.Domain.Projects;

namespace CodeGuardAI.Application.Projects;

public enum ProjectWriteOutcome
{
    Created = 1,
    DuplicateNormalizedRootPath = 2
}

public interface IProjectWriter
{
    Task<ProjectWriteOutcome> CreateAsync(Project project, CancellationToken cancellationToken);
}
