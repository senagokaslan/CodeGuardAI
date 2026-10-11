using System.Text.Json;
using CodeGuardAI.Infrastructure.LLM.GeminiSchemas;
using Xunit;

namespace CodeGuardAI.UnitTests.LLM.GeminiProvider;

public sealed class GeminiDocumentationSchemaTests
{
    [Fact]
    public void Definition_is_strict_and_requires_all_contract_fields()
    {
        using var document = JsonDocument.Parse(GeminiDocumentationSchema.Definition);
        var root = document.RootElement;

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["title", "executiveSummary", "findingSections", "testCaseSections", "conclusion"],
            root.GetProperty("required").EnumerateArray().Select(static item => item.GetString()));
    }

    [Fact]
    public void Parse_accepts_valid_contract_and_rejects_unknown_fields()
    {
        const string valid = """
            {"title":"Report","executiveSummary":"Summary","findingSections":[],"testCaseSections":[],"conclusion":"Conclusion"}
            """;
        const string invalid = """
            {"title":"Report","executiveSummary":"Summary","findingSections":[],"testCaseSections":[],"conclusion":"Conclusion","secret":"must not pass"}
            """;

        Assert.True(GeminiDocumentationSchema.Parse(valid).IsSuccess);
        Assert.True(GeminiDocumentationSchema.Parse(invalid).IsFailure);
    }
}
