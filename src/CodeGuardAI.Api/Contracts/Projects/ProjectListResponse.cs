namespace CodeGuardAI.Api.Contracts.Projects;

public sealed record ProjectListResponse(
    IReadOnlyList<ProjectResponse> Items,
    int Page,
    int PageSize);
