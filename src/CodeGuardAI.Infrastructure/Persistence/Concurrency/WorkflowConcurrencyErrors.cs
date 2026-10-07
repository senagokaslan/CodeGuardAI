using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodeGuardAI.Infrastructure.Persistence.Concurrency;

internal static class WorkflowConcurrencyIndexes
{
    public const string ActiveReviewPerProject = "ux_review_runs_active_project";
    public const string SuccessfulTestGeneration = "ux_ai_model_runs_successful_test_generation";
}

internal static class WorkflowConcurrencyErrors
{
    public static bool IsUniqueViolation(DbUpdateException exception, string indexName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: var constraintName
        } && string.Equals(constraintName, indexName, StringComparison.Ordinal);
}
