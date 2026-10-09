using CodeGuardAI.Application.Documentation;
using CodeGuardAI.Application.Documentation.Models;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;
using Xunit;

namespace CodeGuardAI.UnitTests.Documentation;

public sealed class DocumentationGroundingValidatorTests
{
    private static readonly Guid ReviewId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid FindingId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TestCaseId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private readonly DocumentationGroundingValidator _validator = new();

    [Fact]
    public void Known_identifiers_are_grounded_to_persisted_entities()
    {
        var finding = CreateFinding();
        var testCase = CreateTestCase();
        var candidate = CreateCandidate(
            [new LLMDocumentationFinding(FindingId, "Impact")],
            [new LLMDocumentationTestCase(TestCaseId, "Coverage")]);

        var result = _validator.Validate(candidate, [finding], [testCase], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(finding, Assert.Single(result.Value.Findings).Finding);
        Assert.Same(testCase, Assert.Single(result.Value.TestCases).TestCase);
    }

    [Theory]
    [InlineData(true, false, "documentation.unknown_finding")]
    [InlineData(true, true, "documentation.duplicate_finding")]
    [InlineData(false, false, "documentation.unknown_test_case")]
    [InlineData(false, true, "documentation.duplicate_test_case")]
    public void Unknown_or_duplicate_identifiers_are_rejected(
        bool findingSection,
        bool duplicate,
        string expectedCode)
    {
        var findingId = findingSection ? Guid.NewGuid() : FindingId;
        var testCaseId = findingSection ? TestCaseId : Guid.NewGuid();
        var findingSections = new List<LLMDocumentationFinding>
        {
            new(findingId, "Impact")
        };
        var testSections = new List<LLMDocumentationTestCase>
        {
            new(testCaseId, "Coverage")
        };
        if (duplicate)
        {
            if (findingSection)
            {
                findingSections[0] = new LLMDocumentationFinding(FindingId, "Impact");
                findingSections.Add(new LLMDocumentationFinding(FindingId, "Other impact"));
            }
            else
            {
                testSections[0] = new LLMDocumentationTestCase(TestCaseId, "Coverage");
                testSections.Add(new LLMDocumentationTestCase(TestCaseId, "Other coverage"));
            }
        }

        var result = _validator.Validate(
            CreateCandidate(findingSections, testSections),
            [CreateFinding()],
            [CreateTestCase()],
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public void Report_without_test_suggestions_is_valid()
    {
        var candidate = CreateCandidate(
            [new LLMDocumentationFinding(FindingId, "Impact")],
            []);

        var result = _validator.Validate(
            candidate,
            [CreateFinding()],
            [],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.TestCases);
    }

    [Fact]
    public void Cancellation_is_observed_before_grounding()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => _validator.Validate(
            CreateCandidate([], []),
            [],
            [],
            cancellation.Token));
    }

    private static LLMDocumentationReport CreateCandidate(
        IReadOnlyList<LLMDocumentationFinding> findings,
        IReadOnlyList<LLMDocumentationTestCase> testCases) =>
        new("Technical report", "Summary", findings, testCases, "Conclusion");

    internal static Finding CreateFinding() => Finding.Create(
        FindingId,
        ReviewId,
        FindingSeverity.High,
        FindingCategory.Security,
        "src/Service.cs",
        10,
        12,
        "Unsafe input",
        "Input is not validated",
        "Validate before use",
        0.9m,
        new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));

    internal static TestCase CreateTestCase(string? suggestedCode = null) => TestCase.Create(
        TestCaseId,
        ReviewId,
        TestCaseType.Validation,
        "Reject unsafe input",
        "src/Service.cs",
        "Unsafe input is supplied",
        "Covers input validation",
        suggestedCode,
        new DateTimeOffset(2026, 10, 9, 8, 1, 0, TimeSpan.Zero));
}
