using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Documentation.Models;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;

namespace CodeGuardAI.Application.Documentation;

public sealed record GroundedDocumentationFinding(Finding Finding, string Impact);

public sealed record GroundedDocumentationTestCase(TestCase TestCase, string Coverage);

public sealed record GroundedDocumentationReport(
    string Title,
    string ExecutiveSummary,
    IReadOnlyList<GroundedDocumentationFinding> Findings,
    IReadOnlyList<GroundedDocumentationTestCase> TestCases,
    string Conclusion);

public sealed class DocumentationGroundingValidator
{
    public Result<GroundedDocumentationReport> Validate(
        LLMDocumentationReport candidate,
        IReadOnlyList<Finding> persistedFindings,
        IReadOnlyList<TestCase> persistedTestCases,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(persistedFindings);
        ArgumentNullException.ThrowIfNull(persistedTestCases);
        cancellationToken.ThrowIfCancellationRequested();

        var findingsById = persistedFindings.ToDictionary(finding => finding.Id);
        var testCasesById = persistedTestCases.ToDictionary(testCase => testCase.Id);
        var seenFindingIds = new HashSet<Guid>();
        var groundedFindings = new List<GroundedDocumentationFinding>(candidate.FindingSections.Count);
        foreach (var section in candidate.FindingSections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenFindingIds.Add(section.FindingId))
            {
                return Result.Failure<GroundedDocumentationReport>(
                    DocumentationGroundingErrors.DuplicateFinding);
            }

            if (!findingsById.TryGetValue(section.FindingId, out var finding))
            {
                return Result.Failure<GroundedDocumentationReport>(
                    DocumentationGroundingErrors.UnknownFinding);
            }

            groundedFindings.Add(new GroundedDocumentationFinding(finding, section.Impact));
        }

        if (seenFindingIds.Count != findingsById.Count)
        {
            return Result.Failure<GroundedDocumentationReport>(
                DocumentationGroundingErrors.MissingFinding);
        }

        var seenTestCaseIds = new HashSet<Guid>();
        var groundedTestCases = new List<GroundedDocumentationTestCase>(candidate.TestCaseSections.Count);
        foreach (var section in candidate.TestCaseSections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenTestCaseIds.Add(section.TestCaseId))
            {
                return Result.Failure<GroundedDocumentationReport>(
                    DocumentationGroundingErrors.DuplicateTestCase);
            }

            if (!testCasesById.TryGetValue(section.TestCaseId, out var testCase))
            {
                return Result.Failure<GroundedDocumentationReport>(
                    DocumentationGroundingErrors.UnknownTestCase);
            }

            groundedTestCases.Add(new GroundedDocumentationTestCase(testCase, section.Coverage));
        }

        if (seenTestCaseIds.Count != testCasesById.Count)
        {
            return Result.Failure<GroundedDocumentationReport>(
                DocumentationGroundingErrors.MissingTestCase);
        }

        return Result.Success(new GroundedDocumentationReport(
            candidate.Title,
            candidate.ExecutiveSummary,
            groundedFindings,
            groundedTestCases,
            candidate.Conclusion));
    }
}

public static class DocumentationGroundingErrors
{
    public static readonly Error UnknownFinding = Error.Validation(
        "documentation.unknown_finding",
        "The documentation response references an unknown finding.");

    public static readonly Error DuplicateFinding = Error.Validation(
        "documentation.duplicate_finding",
        "The documentation response references a finding more than once.");

    public static readonly Error MissingFinding = Error.Validation(
        "documentation.missing_finding",
        "The documentation response does not reference every persisted finding.");

    public static readonly Error UnknownTestCase = Error.Validation(
        "documentation.unknown_test_case",
        "The documentation response references an unknown test case.");

    public static readonly Error DuplicateTestCase = Error.Validation(
        "documentation.duplicate_test_case",
        "The documentation response references a test case more than once.");

    public static readonly Error MissingTestCase = Error.Validation(
        "documentation.missing_test_case",
        "The documentation response does not reference every persisted test case.");
}
