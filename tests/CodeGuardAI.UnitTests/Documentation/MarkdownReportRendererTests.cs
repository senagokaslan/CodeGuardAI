using CodeGuardAI.Application.Documentation;
using Xunit;

namespace CodeGuardAI.UnitTests.Documentation;

public sealed class MarkdownReportRendererTests
{
    private readonly MarkdownReportRenderer _renderer = new();

    [Fact]
    public void Renderer_uses_canonical_finding_and_test_fields_without_test_source()
    {
        var finding = DocumentationGroundingValidatorTests.CreateFinding();
        var testCase = DocumentationGroundingValidatorTests.CreateTestCase(
            "SECRET_SOURCE_CODE_SHOULD_NOT_APPEAR");
        var report = new GroundedDocumentationReport(
            "Technical report",
            "Review summary",
            [new GroundedDocumentationFinding(finding, "May expose invalid state")],
            [new GroundedDocumentationTestCase(testCase, "Covers the validation boundary")],
            "Apply the persisted recommendations");

        var result = _renderer.Render(report, 20_000, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("Severity: High", result.Value);
        Assert.Contains("Category: Security", result.Value);
        Assert.Contains("`src/Service.cs:10-12`", result.Value);
        Assert.Contains("Input is not validated", result.Value);
        Assert.Contains("Validate before use", result.Value);
        Assert.Contains("Reject unsafe input", result.Value);
        Assert.DoesNotContain("SECRET_SOURCE_CODE_SHOULD_NOT_APPEAR", result.Value);
        Assert.DoesNotContain("```", result.Value);
    }

    [Fact]
    public void Renderer_supports_a_review_without_findings_or_tests()
    {
        var report = new GroundedDocumentationReport(
            "Clean review",
            "No issues were found",
            [],
            [],
            "No remediation is required");

        var result = _renderer.Render(report, 10_000, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("No persisted findings", result.Value);
        Assert.Contains("No persisted test suggestions", result.Value);
    }

    [Fact]
    public void Renderer_enforces_output_limit()
    {
        var report = new GroundedDocumentationReport(
            "Technical report",
            "Review summary",
            [],
            [],
            "Conclusion");

        var result = _renderer.Render(report, 20, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("documentation.output_limit_exceeded", result.Error.Code);
    }

    [Fact]
    public void Renderer_observes_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => _renderer.Render(
            new GroundedDocumentationReport("Report", "Summary", [], [], "Conclusion"),
            10_000,
            cancellation.Token));
    }
}
