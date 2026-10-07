using CodeGuardAI.Application.Workflows;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Tests;
using CodeGuardAI.Infrastructure.Persistence.Concurrency;
using Microsoft.EntityFrameworkCore;

namespace CodeGuardAI.Infrastructure.Persistence;

internal sealed class EfTestWorkflowStore(CodeGuardDbContext dbContext) : ITestWorkflowStore
{
    public async Task<TestGenerationSource?> GetSourceAsync(
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.ReviewRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == reviewId, cancellationToken);
        if (review is null)
        {
            return null;
        }

        var repositoryRoot = await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.Id == review.ProjectId)
            .Select(project => project.NormalizedRootPath)
            .SingleOrDefaultAsync(cancellationToken);
        if (repositoryRoot is null)
        {
            return null;
        }

        var findings = await dbContext.Findings
            .AsNoTracking()
            .Where(finding => finding.ReviewRunId == reviewId)
            .OrderBy(finding => finding.FilePath)
            .ThenBy(finding => finding.StartLine)
            .ThenBy(finding => finding.Id)
            .ToListAsync(cancellationToken);
        return new TestGenerationSource(review, repositoryRoot, findings);
    }

    public Task<bool> HasCompletedGenerationAsync(
        Guid reviewId,
        CancellationToken cancellationToken) =>
        dbContext.AIModelRuns
            .AsNoTracking()
            .AnyAsync(
                run => run.ReviewRunId == reviewId &&
                       run.Purpose == AIModelRunPurpose.TestGeneration &&
                       run.Status == AIModelRunStatus.Succeeded,
                cancellationToken);

    public async Task<bool> TryCompleteGenerationAsync(
        IReadOnlyCollection<TestCase> testCases,
        AIModelRun modelRun,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.TestCases.AddRange(testCases);
        dbContext.AIModelRuns.Add(modelRun);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            WorkflowConcurrencyErrors.IsUniqueViolation(
                exception,
                WorkflowConcurrencyIndexes.SuccessfulTestGeneration))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            foreach (var entry in dbContext.ChangeTracker.Entries()
                         .Where(entry => entry.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }

            return false;
        }
    }

    public async Task SaveFailedModelRunAsync(
        AIModelRun modelRun,
        CancellationToken cancellationToken)
    {
        dbContext.AIModelRuns.Add(modelRun);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
