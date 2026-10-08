using CodeGuardAI.Domain.Documentation;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Projects;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CodeGuardAI.IntegrationTests.Documentation;

[Trait("Category", "Database")]
[Collection("Database")]
public sealed class DocumentationReportDatabaseTests
{
    private const string ConnectionStringVariable = "CODEGUARD_TEST_DATABASE_CONNECTION_STRING";
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task Duplicate_review_report_is_rejected_by_postgresql_unique_constraint()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var (_, reviewId, modelRunId) = await SeedReviewAsync(context);

        context.DocumentationReports.Add(CreateReport(reviewId, modelRunId));
        await context.SaveChangesAsync();

        var secondModelRun = CreateModelRun(reviewId);
        context.AIModelRuns.Add(secondModelRun);
        context.DocumentationReports.Add(CreateReport(reviewId, secondModelRun.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal("ux_documentation_reports_review_run_id", postgresException.ConstraintName);
    }

    [DatabaseFact]
    public async Task Missing_review_and_model_run_are_rejected_by_foreign_key()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.DocumentationReports.Add(CreateReport(Guid.NewGuid(), Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresException.SqlState);
    }

    [DatabaseFact]
    public async Task Deleting_review_cascades_to_documentation_report_and_model_run()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var (_, reviewId, modelRunId) = await SeedReviewAsync(context);
        var report = CreateReport(reviewId, modelRunId);
        context.DocumentationReports.Add(report);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await context.ReviewRuns
            .Where(review => review.Id == reviewId)
            .ExecuteDeleteAsync();

        Assert.False(await context.DocumentationReports.AnyAsync(candidate => candidate.Id == report.Id));
        Assert.False(await context.AIModelRuns.AnyAsync(candidate => candidate.Id == modelRunId));
    }

    private static CodeGuardDbContext CreateContext()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)!;
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database) ||
            !builder.Database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} must target a database whose name ends with '_test'.");
        }

        var options = new DbContextOptionsBuilder<CodeGuardDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;
        return new CodeGuardDbContext(options);
    }

    private static async Task<(Guid ProjectId, Guid ReviewId, Guid ModelRunId)> SeedReviewAsync(
        CodeGuardDbContext context)
    {
        var projectId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var project = Project.Create(
            projectId,
            "Documentation database test",
            $"C:\\codeguard-tests\\{projectId:N}",
            $"c:\\codeguard-tests\\{projectId:N}",
            CreatedAtUtc);
        var review = ReviewRun.Start(
            reviewId,
            projectId,
            "fake-model",
            "review-v1",
            CreatedAtUtc);
        Assert.True(review.TryComplete(CreatedAtUtc.AddSeconds(1)));
        var modelRun = CreateModelRun(reviewId);

        context.Projects.Add(project);
        context.ReviewRuns.Add(review);
        context.AIModelRuns.Add(modelRun);
        await context.SaveChangesAsync();

        return (projectId, reviewId, modelRun.Id);
    }

    private static AIModelRun CreateModelRun(Guid reviewId)
    {
        return AIModelRun.Create(
            Guid.NewGuid(),
            reviewId,
            AIModelRunPurpose.Documentation,
            "Fake",
            "fake-model",
            "docs-v1",
            10,
            100,
            50,
            AIModelRunStatus.Succeeded,
            null,
            CreatedAtUtc.AddSeconds(2));
    }

    private static DocumentationReport CreateReport(Guid reviewId, Guid modelRunId)
    {
        return DocumentationReport.Create(
            Guid.NewGuid(),
            reviewId,
            modelRunId,
            "Technical report",
            "# Technical report",
            CreatedAtUtc.AddSeconds(3));
    }

    private sealed class DatabaseFactAttribute : FactAttribute
    {
        public DatabaseFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip = $"Set {ConnectionStringVariable} to run PostgreSQL integration tests.";
            }
        }
    }
}
