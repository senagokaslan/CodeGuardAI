using CodeGuardAI.Application.Workflows;
using CodeGuardAI.Domain.Documentation;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Tests;
using CodeGuardAI.Infrastructure.Persistence.Concurrency;
using Microsoft.EntityFrameworkCore;

namespace CodeGuardAI.Infrastructure.Persistence;

internal sealed class EfDocsWorkflowStore(CodeGuardDbContext dbContext) : IDocsWorkflowStore
{
    public async Task<DocsGenerationSource?> GetSourceAsync(
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        var header = await (
                from review in dbContext.ReviewRuns.AsNoTracking()
                join project in dbContext.Projects.AsNoTracking()
                    on review.ProjectId equals project.Id
                where review.Id == reviewId
                select new { ReviewRun = review, ProjectName = project.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (header is null)
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
        var testCaseRows = await dbContext.TestCases
            .AsNoTracking()
            .Where(testCase => testCase.ReviewRunId == reviewId)
            .OrderBy(testCase => testCase.Target)
            .ThenBy(testCase => testCase.Name)
            .ThenBy(testCase => testCase.Id)
            .Select(testCase => new
            {
                testCase.Id,
                testCase.ReviewRunId,
                testCase.Type,
                testCase.Name,
                testCase.Target,
                testCase.Scenario,
                testCase.Reason,
                testCase.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
        var testCases = testCaseRows
            .Select(testCase => TestCase.Create(
                testCase.Id,
                testCase.ReviewRunId,
                testCase.Type,
                testCase.Name,
                testCase.Target,
                testCase.Scenario,
                testCase.Reason,
                suggestedTestCode: null,
                createdAtUtc: testCase.CreatedAtUtc))
            .ToArray();
        return new DocsGenerationSource(
            header.ReviewRun,
            header.ProjectName,
            findings,
            testCases);
    }

    public Task<bool> HasCompletedReportAsync(
        Guid reviewId,
        CancellationToken cancellationToken) =>
        dbContext.DocumentationReports
            .AsNoTracking()
            .AnyAsync(report => report.ReviewRunId == reviewId, cancellationToken);

    public async Task<bool> TryCompleteAsync(
        DocumentationReport report,
        AIModelRun modelRun,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.AIModelRuns.Add(modelRun);
        dbContext.DocumentationReports.Add(report);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            WorkflowConcurrencyErrors.IsUniqueViolation(
                exception,
                WorkflowConcurrencyIndexes.DocumentationReportPerReview))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            Detach(modelRun, report);
            return false;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            Detach(modelRun, report);
            throw;
        }
    }

    public async Task SaveFailedModelRunAsync(
        AIModelRun modelRun,
        CancellationToken cancellationToken)
    {
        dbContext.AIModelRuns.Add(modelRun);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<DocumentationReadModel?> GetAsync(
        Guid reviewId,
        CancellationToken cancellationToken) =>
        dbContext.DocumentationReports
            .AsNoTracking()
            .Where(report => report.ReviewRunId == reviewId)
            .Select(report => new DocumentationReadModel(
                report.Id,
                report.ReviewRunId,
                report.ModelRunId,
                report.Title,
                report.MarkdownContent,
                report.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    private void Detach(AIModelRun modelRun, DocumentationReport report)
    {
        dbContext.Entry(modelRun).State = EntityState.Detached;
        dbContext.Entry(report).State = EntityState.Detached;
    }
}
