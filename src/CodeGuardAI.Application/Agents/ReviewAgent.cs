using System.Text;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Context;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Application.Prompts;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Application.Reviews;
using CodeGuardAI.Application.Reviews.Models;
using CodeGuardAI.Domain.Projects;
using CodeGuardAI.Domain.Reviews;

namespace CodeGuardAI.Application.Agents;

public interface IReviewAgent
{
    Task<Result<ReviewAgentResult>> RunAsync(
        ReviewAgentInput input,
        CancellationToken cancellationToken);
}

public sealed record ReviewAgentInput
{
    public ReviewAgentInput(
        Project project,
        Guid reviewRunId,
        ScanManifest manifest,
        RepositoryContext repositoryContext,
        ReviewAgentPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(repositoryContext);
        ArgumentNullException.ThrowIfNull(policy);
        if (reviewRunId == Guid.Empty)
        {
            throw new ArgumentException("Review run id cannot be empty.", nameof(reviewRunId));
        }

        Project = project;
        ReviewRunId = reviewRunId;
        Manifest = manifest;
        RepositoryContext = repositoryContext;
        Policy = policy;
    }

    public Project Project { get; }

    public Guid ReviewRunId { get; }

    public ScanManifest Manifest { get; }

    public RepositoryContext RepositoryContext { get; }

    public ReviewAgentPolicy Policy { get; }
}

public sealed record ReviewAgentPolicy
{
    public ReviewAgentPolicy(string model, TimeSpan timeout, int maxFindings = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        if (maxFindings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFindings), "Finding limit must be positive.");
        }

        Model = model.Trim();
        Timeout = timeout;
        MaxFindings = maxFindings;
    }

    public string Model { get; }

    public TimeSpan Timeout { get; }

    public int MaxFindings { get; }
}

public sealed record RejectedFinding(
    LLMFinding Candidate,
    FindingRejectionReason Reason);

public sealed record ReviewAgentResult(
    Guid ProjectId,
    Guid ReviewRunId,
    string Model,
    string PromptVersion,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<RejectedFinding> RejectedFindings,
    int DiscardedDuplicateCount);

public sealed class ReviewAgent(
    ILLMProvider provider,
    FindingGroundingValidator groundingValidator,
    TimeProvider timeProvider) : IReviewAgent
{
    public async Task<Result<ReviewAgentResult>> RunAsync(
        ReviewAgentInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new LLMRequest(
            input.Policy.Model,
            ReviewPromptRegistry.CurrentVersion,
            input.Policy.Timeout,
            ReviewPromptRegistry.SystemPrompt,
            ReviewPromptRegistry.CreateTaskPrompt(FormatContext(input)));
        var providerResult = await provider.GenerateReviewAsync(request, cancellationToken);
        if (providerResult.IsFailure)
        {
            return Result.Failure<ReviewAgentResult>(MapProviderError(providerResult.Error));
        }

        if (!providerResult.Response.Model.Equals(request.Model, StringComparison.Ordinal) ||
            !providerResult.Response.PromptVersion.Equals(request.PromptVersion, StringComparison.Ordinal))
        {
            return Result.Failure<ReviewAgentResult>(ReviewAgentErrors.InvalidResponse);
        }

        var parseResult = CodeReviewResultParser.Parse(providerResult.Response.Content);
        if (parseResult.IsFailure)
        {
            return Result.Failure<ReviewAgentResult>(ReviewAgentErrors.InvalidResponse);
        }

        var grounded = new List<LLMFinding>();
        var rejected = new List<RejectedFinding>();
        foreach (var candidate in parseResult.Value.Findings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grounding = groundingValidator.Validate(
                candidate,
                input.Manifest,
                input.RepositoryContext);
            if (grounding.IsGrounded)
            {
                grounded.Add(candidate);
            }
            else
            {
                rejected.Add(new RejectedFinding(candidate, grounding.RejectionReason!.Value));
            }
        }

        var groups = grounded
            .GroupBy(CreateDuplicateKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var selected = groups
            .Select(SelectDeterministicWinner)
            .ToArray();
        foreach (var candidate in selected.Skip(input.Policy.MaxFindings))
        {
            rejected.Add(new RejectedFinding(candidate, FindingRejectionReason.PolicyLimit));
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var findings = selected
            .Take(input.Policy.MaxFindings)
            .Select(candidate => Finding.Create(
                Guid.NewGuid(),
                input.ReviewRunId,
                candidate.Severity,
                candidate.Category,
                candidate.FilePath,
                candidate.StartLine,
                candidate.EndLine,
                candidate.Title,
                candidate.Reason,
                candidate.Suggestion,
                candidate.Confidence,
                createdAtUtc))
            .ToArray();

        return Result.Success(new ReviewAgentResult(
            input.Project.Id,
            input.ReviewRunId,
            request.Model,
            request.PromptVersion,
            findings,
            rejected,
            groups.Sum(group => group.Count() - 1)));
    }

    private static string FormatContext(ReviewAgentInput input)
    {
        var builder = new StringBuilder();
        builder.Append("PROJECT ").AppendLine(input.Project.Name.ReplaceLineEndings(" "));
        foreach (var segment in input.RepositoryContext.Segments)
        {
            builder
                .Append("[")
                .Append(segment.RelativePath)
                .Append(':')
                .Append(segment.StartLine)
                .Append('-')
                .Append(segment.EndLine)
                .Append("] ")
                .AppendLine(segment.Content);
        }

        return builder.ToString().TrimEnd();
    }

    private static string CreateDuplicateKey(LLMFinding candidate)
    {
        return string.Join(
            '\u001f',
            candidate.FilePath,
            candidate.StartLine,
            candidate.EndLine,
            (int)candidate.Category,
            candidate.Title.ToUpperInvariant());
    }

    private static LLMFinding SelectDeterministicWinner(IGrouping<string, LLMFinding> group)
    {
        return group
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenByDescending(candidate => candidate.Severity)
            .ThenBy(candidate => candidate.Reason, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Suggestion, StringComparer.Ordinal)
            .First();
    }

    private static Error MapProviderError(LLMProviderError providerError)
    {
        var code = providerError.Type switch
        {
            LLMProviderErrorType.Timeout => "review_agent.provider_timeout",
            LLMProviderErrorType.RateLimit => "review_agent.provider_rate_limit",
            LLMProviderErrorType.InvalidResponse => "review_agent.provider_invalid_response",
            LLMProviderErrorType.Unavailable => "review_agent.provider_unavailable",
            _ => "review_agent.provider_failure"
        };
        return Error.Failure(code, "The review provider could not complete the request.");
    }
}

public static class ReviewAgentErrors
{
    public static readonly Error InvalidResponse = Error.Validation(
        "review_agent.invalid_response",
        "The review provider response is invalid.");
}
