using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Reviews;
using CodeGuardAI.Application.Reviews.Models;

namespace CodeGuardAI.Infrastructure.LLM.GeminiSchemas;

public static class GeminiReviewSchema
{
    public const string Definition = """
        {
          "type": "object",
          "properties": {
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "severity": { "type": "string", "enum": ["info", "low", "medium", "high", "critical"] },
                  "category": { "type": "string", "enum": ["bug", "security", "maintainability", "performance", "reliability", "codeQuality"] },
                  "filePath": { "type": "string" },
                  "startLine": { "type": "integer", "minimum": 1 },
                  "endLine": { "type": "integer", "minimum": 1 },
                  "title": { "type": "string" },
                  "reason": { "type": "string" },
                  "suggestion": { "type": "string" },
                  "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
                },
                "required": ["severity", "category", "filePath", "startLine", "endLine", "title", "reason", "suggestion", "confidence"],
                "additionalProperties": false
              }
            }
          },
          "required": ["findings"],
          "additionalProperties": false
        }
        """;

    public static Result<CodeReviewResult> Parse(string json)
    {
        return CodeReviewResultParser.Parse(json);
    }
}

public static class GeminiSchemaErrors
{
    public static readonly Error InvalidResponse = ReviewParsingErrors.InvalidResponse;
}
