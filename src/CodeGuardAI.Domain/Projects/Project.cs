using CodeGuardAI.Domain.Common;

namespace CodeGuardAI.Domain.Projects;

public sealed class Project
{
    private Project(
        Guid id,
        string name,
        string repositoryPath,
        string normalizedRootPath,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        RepositoryPath = repositoryPath;
        NormalizedRootPath = normalizedRootPath;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string RepositoryPath { get; }

    public string NormalizedRootPath { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public static Project Create(
        Guid id,
        string name,
        string repositoryPath,
        string normalizedRootPath,
        DateTimeOffset createdAtUtc)
    {
        return new Project(
            DomainGuard.NotEmpty(id, nameof(id)),
            DomainGuard.Required(name, nameof(name)),
            DomainGuard.Required(repositoryPath, nameof(repositoryPath)),
            DomainGuard.Required(normalizedRootPath, nameof(normalizedRootPath)),
            DomainGuard.Utc(createdAtUtc, nameof(createdAtUtc)));
    }
}
