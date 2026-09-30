namespace CodeGuardAI.Application.Projects;

public sealed record CreateProjectCommand(string Name, string RepositoryPath);

public sealed record ProjectReadModel(
    Guid Id,
    string Name,
    string RepositoryPath,
    DateTimeOffset CreatedAtUtc);

public sealed record ProjectPage(
    IReadOnlyList<ProjectReadModel> Items,
    int Page,
    int PageSize);
