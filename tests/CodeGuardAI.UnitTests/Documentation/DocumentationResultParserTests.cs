using CodeGuardAI.Application.Documentation;
using Xunit;

namespace CodeGuardAI.UnitTests.Documentation;

public sealed class DocumentationResultParserTests
{
    private const string FindingId = "11111111-1111-1111-1111-111111111111";
    private const string TestCaseId = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void Valid_strict_json_is_parsed()
    {
        var result = Parse(ValidJson());

        Assert.True(result.IsSuccess);
        Assert.Equal("Technical report", result.Value.Title);
        Assert.Equal(Guid.Parse(FindingId), Assert.Single(result.Value.FindingSections).FindingId);
        Assert.Equal(Guid.Parse(TestCaseId), Assert.Single(result.Value.TestCaseSections).TestCaseId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"title\":\"Report\"}")]
    [InlineData("{\"title\":\"Report\",\"executiveSummary\":\"Summary\",\"findingSections\":[],\"testCaseSections\":[],\"conclusion\":\"Done\",\"unknown\":true}")]
    [InlineData("{\"Title\":\"Report\",\"executiveSummary\":\"Summary\",\"findingSections\":[],\"testCaseSections\":[],\"conclusion\":\"Done\"}")]
    [InlineData("{\"title\":\"Report\",\"executiveSummary\":\"Summary\",\"findingSections\":[],\"testCaseSections\":[],\"conclusion\":\"Done\",}")]
    public void Non_conforming_json_fails_closed(string json)
    {
        var result = Parse(json);

        Assert.True(result.IsFailure);
        Assert.Equal("documentation.invalid_response", result.Error.Code);
    }

    [Fact]
    public void Provider_output_character_limit_is_enforced_before_deserialization()
    {
        var json = ValidJson();

        var result = DocumentationResultParser.Parse(json, json.Length - 1, 10, 10);

        Assert.True(result.IsFailure);
        Assert.Equal("documentation.invalid_response", result.Error.Code);
    }

    [Fact]
    public void Finding_and_test_item_limits_are_enforced()
    {
        var findingResult = DocumentationResultParser.Parse(ValidJson(), 10_000, 0, 1);
        var testResult = DocumentationResultParser.Parse(ValidJson(), 10_000, 1, 0);

        Assert.True(findingResult.IsFailure);
        Assert.True(testResult.IsFailure);
    }

    private static CodeGuardAI.Application.Common.Result<
        CodeGuardAI.Application.Documentation.Models.LLMDocumentationReport> Parse(string json) =>
        DocumentationResultParser.Parse(json, 10_000, 10, 10);

    private static string ValidJson() => $$"""
        {
          "title": "Technical report",
          "executiveSummary": "Summary",
          "findingSections": [
            { "findingId": "{{FindingId}}", "impact": "Impact" }
          ],
          "testCaseSections": [
            { "testCaseId": "{{TestCaseId}}", "coverage": "Coverage" }
          ],
          "conclusion": "Done"
        }
        """;
}
