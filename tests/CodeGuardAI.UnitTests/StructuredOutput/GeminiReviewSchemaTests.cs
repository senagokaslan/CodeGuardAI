using System.Text.Json;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Infrastructure.LLM.GeminiSchemas;
using Xunit;

namespace CodeGuardAI.UnitTests.StructuredOutput;

public sealed class GeminiReviewSchemaTests
{
    private const string ValidJson = """
        {
          "findings": [
            {
              "severity": "high",
              "category": "security",
              "filePath": "src/Auth.cs",
              "startLine": 12,
              "endLine": 14,
              "title": "Missing authorization check",
              "reason": "The operation is executed without checking the caller.",
              "suggestion": "Validate authorization before executing the operation.",
              "confidence": 0.95
            }
          ]
        }
        """;

    [Fact]
    public void Valid_fixture_is_strictly_parsed_with_string_enums()
    {
        var result = GeminiReviewSchema.Parse(ValidJson);

        Assert.True(result.IsSuccess);
        var finding = Assert.Single(result.Value.Findings);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal(FindingCategory.Security, finding.Category);
        Assert.Equal("src/Auth.cs", finding.FilePath);
        Assert.Equal(12, finding.StartLine);
        Assert.Equal(14, finding.EndLine);
        Assert.Equal(0.95m, finding.Confidence);
    }

    [Theory]
    [MemberData(nameof(InvalidFixtures))]
    public void Invalid_fixture_is_rejected_fail_closed(string json)
    {
        var result = GeminiReviewSchema.Parse(json);

        Assert.True(result.IsFailure);
        Assert.Equal("llm.invalid_response", result.Error.Code);
    }

    [Fact]
    public void Published_schema_uses_string_enums_required_fields_and_no_unknown_properties()
    {
        using var schema = JsonDocument.Parse(GeminiReviewSchema.Definition);
        var root = schema.RootElement;
        var finding = root
            .GetProperty("properties")
            .GetProperty("findings")
            .GetProperty("items");
        var severity = finding
            .GetProperty("properties")
            .GetProperty("severity");

        Assert.Equal("string", severity.GetProperty("type").GetString());
        Assert.Contains(
            severity.GetProperty("enum").EnumerateArray().Select(value => value.GetString()),
            value => value == "critical");
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.False(finding.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(
            finding.GetProperty("required").EnumerateArray().Select(value => value.GetString()),
            value => value == "confidence");
    }

    public static TheoryData<string> InvalidFixtures => new()
    {
        "{\"findings\":[}",
        "{}",
        "{\"findings\":[],\"unexpected\":true}",
        ValidJson.Replace("\"confidence\": 0.95", "\"unexpected\": true", StringComparison.Ordinal),
        "{\"findings\":[{\"severity\":\"high\",\"category\":\"security\",\"filePath\":\"src/Auth.cs\",\"startLine\":12,\"endLine\":14,\"title\":\"Title\",\"reason\":\"Reason\",\"suggestion\":\"Suggestion\"}]}",
        ValidJson.Replace("\"severity\": \"high\"", "\"severity\": 4", StringComparison.Ordinal),
        ValidJson.Replace("\"severity\": \"high\"", "\"severity\": \"urgent\"", StringComparison.Ordinal),
        ValidJson.Replace("\"confidence\": 0.95", "\"confidence\": 1.01", StringComparison.Ordinal),
        ValidJson.Replace("\"startLine\": 12", "\"startLine\": 0", StringComparison.Ordinal),
        ValidJson.Replace("\"endLine\": 14", "\"endLine\": 10", StringComparison.Ordinal),
        ValidJson.Replace("src/Auth.cs", "../Auth.cs", StringComparison.Ordinal),
        "{\"findings\":[null]}",
        "null",
        ""
    };
}
