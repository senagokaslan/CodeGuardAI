using CodeGuardAI.Application.Common;

namespace CodeGuardAI.Application.Projects;

public static class ProjectErrors
{
    public static readonly Error InvalidName = Error.Validation(
        "project.name_invalid",
        $"Project name must be between 1 and {ProjectLimits.NameMaxLength} characters.");

    public static readonly Error InvalidRepositoryPath = Error.Validation(
        "project.repository_path_invalid",
        $"Repository path must resolve to an absolute path no longer than {ProjectLimits.RepositoryPathMaxLength} characters.");

    public static readonly Error DuplicateRepositoryPath = Error.Conflict(
        "project.repository_path_conflict",
        "A project with the same normalized repository path already exists.");

    public static readonly Error NotFound = Error.NotFound(
        "project.not_found",
        "Project was not found.");

    public static readonly Error InvalidPaging = Error.Validation(
        "project.paging_invalid",
        $"Page must be positive and page size must be between 1 and {ProjectLimits.MaxPageSize}.");
}
