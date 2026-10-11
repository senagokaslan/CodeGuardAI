using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Documentation;
using CodeGuardAI.Application.Documentation.Models;

namespace CodeGuardAI.Infrastructure.LLM.GeminiSchemas;

public static class GeminiDocumentationSchema
{
    private const int MaxOutputCharacters = 100_000;
    private const int MaxFindingItems = 100;
    private const int MaxTestCaseItems = 50;

    public const string Definition = """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "minLength": 1, "maxLength": 200 },
            "executiveSummary": { "type": "string", "minLength": 1, "maxLength": 4000 },
            "findingSections": {
              "type": "array",
              "maxItems": 100,
              "items": {
                "type": "object",
                "properties": {
                  "findingId": { "type": "string" },
                  "impact": { "type": "string", "minLength": 1, "maxLength": 2000 }
                },
                "required": ["findingId", "impact"],
                "additionalProperties": false
              }
            },
            "testCaseSections": {
              "type": "array",
              "maxItems": 50,
              "items": {
                "type": "object",
                "properties": {
                  "testCaseId": { "type": "string" },
                  "coverage": { "type": "string", "minLength": 1, "maxLength": 2000 }
                },
                "required": ["testCaseId", "coverage"],
                "additionalProperties": false
              }
            },
            "conclusion": { "type": "string", "minLength": 1, "maxLength": 4000 }
          },
          "required": ["title", "executiveSummary", "findingSections", "testCaseSections", "conclusion"],
          "additionalProperties": false
        }
        """;

    public static Result<LLMDocumentationReport> Parse(string json) =>
        DocumentationResultParser.Parse(
            json,
            MaxOutputCharacters,
            MaxFindingItems,
            MaxTestCaseItems);
}
