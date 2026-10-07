using System.Text.Json;
using CodeGuardAI.Application.Workflows;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Projects;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Infrastructure.Persistence.Concurrency;
using Microsoft.EntityFrameworkCore;

namespace CodeGuardAI.Infrastructure.Persistence;

internal sealed class EfReviewWorkflowStore(CodeGuardDbContext dbContext) : IReviewWorkflowStore
{
    public Task<Project?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        return dbContext.Projects.SingleOrDefaultAsync(
            project => project.Id == projectId,
            cancellationToken);
    }

    public async Task<bool> TryCreatePendingAsync(
        ReviewRun reviewRun,
        DateTimeOffset staleBeforeUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.ReviewRuns
            .Where(candidate =>
                candidate.ProjectId == reviewRun.ProjectId &&
                candidate.StartedAtUtc < staleBeforeUtc &&
                (candidate.Status == ReviewStatus.Pending || candidate.Status == ReviewStatus.Running))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.Status, ReviewStatus.Failed)
                    .SetProperty(candidate => candidate.ErrorCode, ReviewRun.RecoveryTimeoutErrorCode)
                    .SetProperty(candidate => candidate.CompletedAtUtc, reviewRun.StartedAtUtc),
                cancellationToken);

        dbContext.ReviewRuns.Add(reviewRun);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            WorkflowConcurrencyErrors.IsUniqueViolation(
                exception,
                WorkflowConcurrencyIndexes.ActiveReviewPerProject))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.Entry(reviewRun).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveStateAsync(ReviewRun reviewRun, CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        ReviewRun reviewRun,
        IReadOnlyCollection<Finding> findings,
        AIModelRun modelRun,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Findings.AddRange(findings);
        dbContext.AIModelRuns.Add(modelRun);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FailAsync(
        ReviewRun reviewRun,
        AIModelRun? modelRun,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (modelRun is not null)
        {
            dbContext.AIModelRuns.Add(modelRun);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ReviewReadModel?> GetAsync(
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.ReviewRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == reviewId, cancellationToken);
        return review is null ? null : await ToReadModelAsync(review, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewReadModel>> GetHistoryAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var reviews = await dbContext.ReviewRuns
            .AsNoTracking()
            .Where(review => review.ProjectId == projectId)
            .OrderByDescending(review => review.StartedAtUtc)
            .ThenByDescending(review => review.Id)
            .ToListAsync(cancellationToken);
        var result = new List<ReviewReadModel>(reviews.Count);
        foreach (var review in reviews)
        {
            result.Add(await ToReadModelAsync(review, cancellationToken));
        }

        return result;
    }

    private async Task<ReviewReadModel> ToReadModelAsync(
        ReviewRun review,
        CancellationToken cancellationToken)
    {
        var findings = await dbContext.Findings
            .AsNoTracking()
            .Where(finding => finding.ReviewRunId == review.Id)
            .OrderBy(finding => finding.FilePath)
            .ThenBy(finding => finding.StartLine)
            .ThenBy(finding => finding.Id)
            .Select(finding => new ReviewFindingReadModel(
                finding.Id,
                finding.FilePath,
                finding.StartLine,
                finding.EndLine,
                finding.Severity,
                finding.Category,
                finding.Title,
                finding.Reason,
                finding.Suggestion,
                finding.Confidence))
            .ToListAsync(cancellationToken);
        return new ReviewReadModel(
            review.Id,
            review.ProjectId,
            review.Status,
            review.ModelName,
            review.PromptVersion,
            review.StartedAtUtc,
            review.CompletedAtUtc,
            review.ErrorCode,
            ParseScanSummary(review.ScanSummaryJson),
            findings);
    }

    private static ReviewScanSummaryReadModel? ParseScanSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReviewScanSummaryReadModel>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
