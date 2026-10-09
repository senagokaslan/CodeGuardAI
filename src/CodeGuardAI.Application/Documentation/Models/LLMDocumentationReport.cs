using System.Text.Json.Serialization;

namespace CodeGuardAI.Application.Documentation.Models;

public sealed record LLMDocumentationReport
{
    public const int MaxTitleLength = 200;
    public const int MaxSummaryLength = 4_000;
    public const int MaxConclusionLength = 4_000;
    public const int MaxSectionTextLength = 2_000;

    [JsonConstructor]
    public LLMDocumentationReport(
        string title,
        string executiveSummary,
        IReadOnlyList<LLMDocumentationFinding> findingSections,
        IReadOnlyList<LLMDocumentationTestCase> testCaseSections,
        string conclusion)
    {
        ArgumentNullException.ThrowIfNull(findingSections);
        ArgumentNullException.ThrowIfNull(testCaseSections);
        if (findingSections.Any(static item => item is null))
        {
            throw new ArgumentException("Finding sections cannot contain null entries.", nameof(findingSections));
        }

        if (testCaseSections.Any(static item => item is null))
        {
            throw new ArgumentException("Test case sections cannot contain null entries.", nameof(testCaseSections));
        }

        Title = RequiredBounded(title, MaxTitleLength, nameof(title));
        ExecutiveSummary = RequiredBounded(
            executiveSummary,
            MaxSummaryLength,
            nameof(executiveSummary));
        FindingSections = findingSections.ToArray();
        TestCaseSections = testCaseSections.ToArray();
        Conclusion = RequiredBounded(conclusion, MaxConclusionLength, nameof(conclusion));
    }

    public string Title { get; }

    public string ExecutiveSummary { get; }

    public IReadOnlyList<LLMDocumentationFinding> FindingSections { get; }

    public IReadOnlyList<LLMDocumentationTestCase> TestCaseSections { get; }

    public string Conclusion { get; }

    internal static string RequiredBounded(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                normalized.Length,
                $"The value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}

public sealed record LLMDocumentationFinding
{
    [JsonConstructor]
    public LLMDocumentationFinding(Guid findingId, string impact)
    {
        if (findingId == Guid.Empty)
        {
            throw new ArgumentException("Finding id cannot be empty.", nameof(findingId));
        }

        FindingId = findingId;
        Impact = LLMDocumentationReport.RequiredBounded(
            impact,
            LLMDocumentationReport.MaxSectionTextLength,
            nameof(impact));
    }

    public Guid FindingId { get; }

    public string Impact { get; }
}

public sealed record LLMDocumentationTestCase
{
    [JsonConstructor]
    public LLMDocumentationTestCase(Guid testCaseId, string coverage)
    {
        if (testCaseId == Guid.Empty)
        {
            throw new ArgumentException("Test case id cannot be empty.", nameof(testCaseId));
        }

        TestCaseId = testCaseId;
        Coverage = LLMDocumentationReport.RequiredBounded(
            coverage,
            LLMDocumentationReport.MaxSectionTextLength,
            nameof(coverage));
    }

    public Guid TestCaseId { get; }

    public string Coverage { get; }
}
