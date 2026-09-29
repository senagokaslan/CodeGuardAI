namespace CodeGuardAI.Api.Contracts.Projects;

public sealed record ProjectResponse(Guid Id, string Name, string RepositoryPath);
