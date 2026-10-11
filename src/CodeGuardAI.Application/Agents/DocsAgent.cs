using System.Text.Json;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Documentation;
using CodeGuardAI.Application.Documentation.Models;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Application.Prompts;
using CodeGuardAI.Domain.Documentation;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;

namespace CodeGuardAI.Application.Agents;

public interface IDocsAgent
{
    string ProviderName { get; }

    Task<Result<DocsAgentResult>> RunAsync(
        DocsAgentInput input,
        CancellationToken cancellationToken);
}

public sealed record DocsAgentInput
{
    public DocsAgentInput(
        ReviewRun reviewRun,
        string projectName,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<TestCase> testCases,
        DocsAgentPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(reviewRun);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(testCases);
        ArgumentNullException.ThrowIfNull(policy);
        if (findings.Any(static finding => finding is null))
        {
            throw new ArgumentException("Findings cannot contain null entries.", nameof(findings));
        }

        if (testCases.Any(static testCase => testCase is null))
        {
            throw new ArgumentException("Test cases cannot contain null entries.", nameof(testCases));
        }

        ReviewRun = reviewRun;
        ProjectName = projectName.Trim();
        Findings = findings.ToArray();
        TestCases = testCases.ToArray();
        Policy = policy;
    }

    public ReviewRun ReviewRun { get; }

    public string ProjectName { get; }

    public IReadOnlyList<Finding> Findings { get; }

    public IReadOnlyList<TestCase> TestCases { get; }

    public DocsAgentPolicy Policy { get; }
}

public sealed record DocsAgentPolicy
{
    public DocsAgentPolicy(
        string model,
        TimeSpan timeout,
        int maxFindings = 100,
        int maxTestCases = 50,
        int maxProviderOutputCharacters = 100_000,
        int maxReportCharacters = DocumentationReport.MaxMarkdownContentLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        if (maxFindings < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFindings), "Finding limit cannot be negative.");
        }

        if (maxTestCases < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTestCases), "Test case limit cannot be negative.");
        }

        if (maxProviderOutputCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxProviderOutputCharacters),
                "Provider output limit must be positive.");
        }

        if (maxReportCharacters is <= 0 or > DocumentationReport.MaxMarkdownContentLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxReportCharacters),
                $"Report limit must be between 1 and {DocumentationReport.MaxMarkdownContentLength}.");
        }

        Model = model.Trim();
        Timeout = timeout;
        MaxFindings = maxFindings;
        MaxTestCases = maxTestCases;
        MaxProviderOutputCharacters = maxProviderOutputCharacters;
        MaxReportCharacters = maxReportCharacters;
    }

    public string Model { get; }

    public TimeSpan Timeout { get; }

    public int MaxFindings { get; }

    public int MaxTestCases { get; }

    public int MaxProviderOutputCharacters { get; }

    public int MaxReportCharacters { get; }
}

public sealed record DocsAgentResult(
    Guid ReviewRunId,
    string Model,
    string PromptVersion,
    string Provider,
    TimeSpan Duration,
    int InputCharacters,
    int OutputCharacters,
    GroundedDocumentationReport Report,
    int FindingCount,
    int TestCaseCount);

public sealed class DocsAgent(
    ILLMProvider provider,
    DocumentationGroundingValidator groundingValidator) : IDocsAgent
{
    private static readonly JsonSerializerOptions PromptSerializerOptions = new(JsonSerializerDefaults.Web);

    public string ProviderName => provider.Name;

    public async Task<Result<DocsAgentResult>> RunAsync(
        DocsAgentInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var inputValidation = ValidateInput(input);
        if (inputValidation != Error.None)
        {
            return Result.Failure<DocsAgentResult>(inputValidation);
        }

        var promptInput = FormatInput(input);
        var request = new LLMRequest(
            input.Policy.Model,
            DocsPromptRegistry.CurrentVersion,
            input.Policy.Timeout,
            DocsPromptRegistry.SystemPrompt,
            DocsPromptRegistry.CreateTaskPrompt(promptInput));
        var providerResult = await provider.GenerateDocumentationAsync(request, cancellationToken);
        if (providerResult.IsFailure)
        {
            return Result.Failure<DocsAgentResult>(MapProviderError(providerResult.Error));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!providerResult.Response.Model.Equals(request.Model, StringComparison.Ordinal) ||
            !providerResult.Response.PromptVersion.Equals(request.PromptVersion, StringComparison.Ordinal))
        {
            return Result.Failure<DocsAgentResult>(DocsAgentErrors.InvalidResponse);
        }

        var parseResult = DocumentationResultParser.Parse(
            providerResult.Response.Content,
            input.Policy.MaxProviderOutputCharacters,
            input.Policy.MaxFindings,
            input.Policy.MaxTestCases);
        if (parseResult.IsFailure)
        {
            return Result.Failure<DocsAgentResult>(DocsAgentErrors.InvalidResponse);
        }

        if (ContainsRawPrompt(parseResult.Value, request))
        {
            return Result.Failure<DocsAgentResult>(DocsAgentErrors.InvalidResponse);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var groundingResult = groundingValidator.Validate(
            parseResult.Value,
            input.Findings,
            input.TestCases,
            cancellationToken);
        if (groundingResult.IsFailure)
        {
            return Result.Failure<DocsAgentResult>(groundingResult.Error);
        }

        return Result.Success(new DocsAgentResult(
            input.ReviewRun.Id,
            request.Model,
            request.PromptVersion,
            providerResult.Response.Provider,
            providerResult.Response.Duration,
            request.SystemPrompt.Length + request.UserPrompt.Length,
            providerResult.Response.Content.Length,
            groundingResult.Value,
            input.Findings.Count,
            input.TestCases.Count));
    }

    private static Error ValidateInput(DocsAgentInput input)
    {
        if (input.ReviewRun.Status != ReviewStatus.Completed)
        {
            return DocsAgentErrors.ReviewNotCompleted;
        }

        if (input.Findings.Count > input.Policy.MaxFindings ||
            input.TestCases.Count > input.Policy.MaxTestCases)
        {
            return DocsAgentErrors.InputLimitExceeded;
        }

        if (input.Findings.Any(finding => finding.ReviewRunId != input.ReviewRun.Id) ||
            input.Findings.Select(finding => finding.Id).Distinct().Count() != input.Findings.Count ||
            input.TestCases.Any(testCase => testCase.ReviewRunId != input.ReviewRun.Id) ||
            input.TestCases.Select(testCase => testCase.Id).Distinct().Count() != input.TestCases.Count)
        {
            return DocsAgentErrors.InvalidPersistedInput;
        }

        return Error.None;
    }

    private static string FormatInput(DocsAgentInput input)
    {
        var promptData = new
        {
            reviewRunId = input.ReviewRun.Id,
            projectName = input.ProjectName,
            findings = input.Findings
                .OrderBy(finding => finding.Id)
                .Select(finding => new
                {
                    findingId = finding.Id,
                    severity = finding.Severity.ToString(),
                    category = finding.Category.ToString(),
                    file = finding.FilePath,
                    startLine = finding.StartLine,
                    endLine = finding.EndLine,
                    title = finding.Title,
                    reason = finding.Reason,
                    suggestion = finding.Suggestion
                }),
            testCases = input.TestCases
                .OrderBy(testCase => testCase.Id)
                .Select(testCase => new
                {
                    testCaseId = testCase.Id,
                    type = testCase.Type.ToString(),
                    name = testCase.Name,
                    target = testCase.Target,
                    scenario = testCase.Scenario,
                    reason = testCase.Reason
                })
        };

        return JsonSerializer.Serialize(promptData, PromptSerializerOptions);
    }

    private static bool ContainsRawPrompt(
        LLMDocumentationReport report,
        LLMRequest request)
    {
        return ContainsPrompt(report.Title, request) ||
            ContainsPrompt(report.ExecutiveSummary, request) ||
            report.FindingSections.Any(section => ContainsPrompt(section.Impact, request)) ||
            report.TestCaseSections.Any(section => ContainsPrompt(section.Coverage, request)) ||
            ContainsPrompt(report.Conclusion, request);
    }

    private static bool ContainsPrompt(string value, LLMRequest request) =>
        value.Contains(request.SystemPrompt, StringComparison.Ordinal) ||
        value.Contains(request.UserPrompt, StringComparison.Ordinal);

    private static Error MapProviderError(LLMProviderError providerError)
    {
        var code = providerError.Type switch
        {
            LLMProviderErrorType.Timeout => "docs_agent.provider_timeout",
            LLMProviderErrorType.RateLimit => "docs_agent.provider_rate_limit",
            LLMProviderErrorType.InvalidResponse => "docs_agent.provider_invalid_response",
            LLMProviderErrorType.Unavailable => "docs_agent.provider_unavailable",
            _ => "docs_agent.provider_failure"
        };
        return Error.Failure(code, "The documentation provider could not complete the request.");
    }
}

public static class DocsAgentErrors
{
    public static readonly Error ReviewNotCompleted = Error.Conflict(
        "docs_agent.review_not_completed",
        "Documentation requires a completed review.");

    public static readonly Error InputLimitExceeded = Error.Validation(
        "docs_agent.input_limit_exceeded",
        "The persisted review data exceeds the configured documentation limits.");

    public static readonly Error InvalidPersistedInput = Error.Validation(
        "docs_agent.invalid_persisted_input",
        "Findings and test cases must uniquely belong to the completed review.");

    public static readonly Error InvalidResponse = Error.Validation(
        "docs_agent.invalid_response",
        "The documentation provider response is invalid.");
}
