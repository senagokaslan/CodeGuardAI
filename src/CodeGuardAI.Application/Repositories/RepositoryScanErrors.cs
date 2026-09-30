using CodeGuardAI.Application.Common;

namespace CodeGuardAI.Application.Repositories;

public static class RepositoryScanErrors
{
    public static readonly Error InvalidRoot = Error.Validation(
        "repository.root_invalid",
        "Repository root must be an existing, accessible directory.");

    public static readonly Error InvalidPolicy = Error.Failure(
        "repository.scan_policy_invalid",
        "Repository scan limits must be positive.");
}
