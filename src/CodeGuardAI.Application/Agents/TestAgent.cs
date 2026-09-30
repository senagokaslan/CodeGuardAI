using System.Text;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Context;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Application.Prompts;
using CodeGuardAI.Application.Tests;
using CodeGuardAI.Application.Tests.Models;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;

namespace CodeGuardAI.Application.Agents;

public interface ITestAgent
{
    string ProviderName { get; }

    Task<Result<TestAgentResult>> RunAsync(
        TestAgentInput input,
        CancellationToken cancellationToken);
}

public sealed record TestAgentInput
{
    public TestAgentInput(
        ReviewRun reviewRun,
        IReadOnlyList<Finding> selectedFindings,
        RepositoryContext relevantContext,
        TestAgentPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(reviewRun);
        ArgumentNullException.ThrowIfNull(selectedFindings);
        ArgumentNullException.ThrowIfNull(relevantContext);
        ArgumentNullException.ThrowIfNull(policy);

        ReviewRun = reviewRun;
        SelectedFindings = selectedFindings;
        RelevantContext = relevantContext;
        Policy = policy;
    }

    public ReviewRun ReviewRun { get; }

    public IReadOnlyList<Finding> SelectedFindings { get; }

    public RepositoryContext RelevantContext { get; }

    public TestAgentPolicy Policy { get; }
}

public sealed record TestAgentPolicy
{
    public TestAgentPolicy(string model, TimeSpan timeout, int maxSuggestions = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        if (maxSuggestions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSuggestions), "Suggestion limit must be positive.");
        }

        Model = model.Trim();
        Timeout = timeout;
        MaxSuggestions = maxSuggestions;
    }

    public string Model { get; }

    public TimeSpan Timeout { get; }

    public int MaxSuggestions { get; }
}

public enum TestSuggestionRejectionReason
{
    UnknownTarget = 1,
    PolicyLimit = 2
}

public sealed record RejectedTestSuggestion(
    LLMTestCase Candidate,
    TestSuggestionRejectionReason Reason);

public sealed record TestAgentResult(
    Guid ReviewRunId,
    string Model,
    string PromptVersion,
    string Provider,
    TimeSpan Duration,
    IReadOnlyList<TestCase> TestCases,
    IReadOnlyList<RejectedTestSuggestion> RejectedSuggestions,
    int DiscardedDuplicateCount);

public sealed class TestAgent(
    ILLMProvider provider,
    TimeProvider timeProvider) : ITestAgent
{
    public string ProviderName => provider.Name;

    public async Task<Result<TestAgentResult>> RunAsync(
        TestAgentInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var inputValidation = ValidateInput(input);
        if (inputValidation != Error.None)
        {
            return Result.Failure<TestAgentResult>(inputValidation);
        }

        var request = new LLMRequest(
            input.Policy.Model,
            TestPromptRegistry.CurrentVersion,
            input.Policy.Timeout,
            TestPromptRegistry.SystemPrompt,
            TestPromptRegistry.CreateTaskPrompt(FormatInput(input)));
        var providerResult = await provider.GenerateTestsAsync(request, cancellationToken);
        if (providerResult.IsFailure)
        {
            return Result.Failure<TestAgentResult>(MapProviderError(providerResult.Error));
        }

        if (!providerResult.Response.Model.Equals(request.Model, StringComparison.Ordinal) ||
            !providerResult.Response.PromptVersion.Equals(request.PromptVersion, StringComparison.Ordinal))
        {
            return Result.Failure<TestAgentResult>(TestAgentErrors.InvalidResponse);
        }

        var parseResult = TestGenerationResultParser.Parse(providerResult.Response.Content);
        if (parseResult.IsFailure)
        {
            return Result.Failure<TestAgentResult>(TestAgentErrors.InvalidResponse);
        }

        var allowedTargets = input.SelectedFindings
            .Select(finding => finding.FilePath)
            .Intersect(
                input.RelevantContext.Segments
                    .Where(segment => !segment.IsTruncationMarker)
                    .Select(segment => segment.RelativePath),
                StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var grounded = new List<LLMTestCase>();
        var rejected = new List<RejectedTestSuggestion>();
        foreach (var candidate in parseResult.Value.Tests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!allowedTargets.Contains(candidate.Target))
            {
                rejected.Add(new RejectedTestSuggestion(
                    candidate,
                    TestSuggestionRejectionReason.UnknownTarget));
                continue;
            }

            grounded.Add(candidate);
        }

        var groups = grounded
            .GroupBy(CreateDuplicateKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var selected = groups.Select(SelectDeterministicWinner).ToArray();
        foreach (var candidate in selected.Skip(input.Policy.MaxSuggestions))
        {
            rejected.Add(new RejectedTestSuggestion(
                candidate,
                TestSuggestionRejectionReason.PolicyLimit));
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var testCases = selected
            .Take(input.Policy.MaxSuggestions)
            .Select(candidate => TestCase.Create(
                Guid.NewGuid(),
                input.ReviewRun.Id,
                candidate.Type,
                candidate.Name,
                candidate.Target,
                candidate.Scenario,
                candidate.Reason,
                candidate.SuggestedTestCode,
                createdAtUtc))
            .ToArray();

        return Result.Success(new TestAgentResult(
            input.ReviewRun.Id,
            request.Model,
            request.PromptVersion,
            providerResult.Response.Provider,
            providerResult.Response.Duration,
            testCases,
            rejected,
            groups.Sum(group => group.Count() - 1)));
    }

    private static Error ValidateInput(TestAgentInput input)
    {
        if (input.ReviewRun.Status != ReviewStatus.Completed)
        {
            return TestAgentErrors.ReviewNotCompleted;
        }

        if (input.SelectedFindings.Count == 0 ||
            input.SelectedFindings.Any(finding => finding.ReviewRunId != input.ReviewRun.Id))
        {
            return TestAgentErrors.InvalidSelection;
        }

        return Error.None;
    }

    private static string FormatInput(TestAgentInput input)
    {
        var builder = new StringBuilder();
        var selectedTargets = input.SelectedFindings
            .Select(finding => finding.FilePath)
            .ToHashSet(StringComparer.Ordinal);
        builder.AppendLine("SELECTED_FINDINGS");
        foreach (var finding in input.SelectedFindings.OrderBy(finding => finding.Id))
        {
            builder
                .Append('[').Append(finding.FilePath).Append(':')
                .Append(finding.StartLine).Append('-').Append(finding.EndLine).Append("] ")
                .Append(finding.Title.ReplaceLineEndings(" ")).Append(" | ")
                .Append(finding.Reason.ReplaceLineEndings(" ")).Append(" | ")
                .AppendLine(finding.Suggestion.ReplaceLineEndings(" "));
        }

        builder.AppendLine("REPOSITORY_CONTEXT");
        foreach (var segment in input.RelevantContext.Segments
                     .Where(segment => selectedTargets.Contains(segment.RelativePath)))
        {
            builder
                .Append('[').Append(segment.RelativePath).Append(':')
                .Append(segment.StartLine).Append('-').Append(segment.EndLine).Append("] ")
                .AppendLine(segment.Content);
        }

        return builder.ToString().TrimEnd();
    }

    private static string CreateDuplicateKey(LLMTestCase candidate)
    {
        return string.Join(
            '\u001f',
            candidate.Target,
            (int)candidate.Type,
            candidate.Name.ToUpperInvariant(),
            candidate.Scenario.ToUpperInvariant());
    }

    private static LLMTestCase SelectDeterministicWinner(IGrouping<string, LLMTestCase> group)
    {
        return group
            .OrderByDescending(candidate => candidate.SuggestedTestCode is not null)
            .ThenBy(candidate => candidate.Reason, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SuggestedTestCode, StringComparer.Ordinal)
            .First();
    }

    private static Error MapProviderError(LLMProviderError providerError)
    {
        var code = providerError.Type switch
        {
            LLMProviderErrorType.Timeout => "test_agent.provider_timeout",
            LLMProviderErrorType.RateLimit => "test_agent.provider_rate_limit",
            LLMProviderErrorType.InvalidResponse => "test_agent.provider_invalid_response",
            LLMProviderErrorType.Unavailable => "test_agent.provider_unavailable",
            _ => "test_agent.provider_failure"
        };
        return Error.Failure(code, "The test generation provider could not complete the request.");
    }
}

public static class TestAgentErrors
{
    public static readonly Error ReviewNotCompleted = Error.Conflict(
        "test_agent.review_not_completed",
        "Test suggestions require a completed review.");

    public static readonly Error InvalidSelection = Error.Validation(
        "test_agent.invalid_selection",
        "Selected findings must belong to the completed review.");

    public static readonly Error InvalidResponse = Error.Validation(
        "test_agent.invalid_response",
        "The test generation provider response is invalid.");
}
