using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Tests;
using CodeGuardAI.Application.Tests.Models;

namespace CodeGuardAI.Infrastructure.LLM.GeminiSchemas;

public static class GeminiTestGenerationSchema
{
    public const string Definition = """
        {
          "type": "object",
          "properties": {
            "tests": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "type": { "type": "string", "enum": ["unit", "edgeCase", "regression", "validation"] },
                  "name": { "type": "string" },
                  "target": { "type": "string" },
                  "scenario": { "type": "string" },
                  "reason": { "type": "string" },
                  "suggestedTestCode": { "type": "string" }
                },
                "required": ["type", "name", "target", "scenario", "reason"],
                "additionalProperties": false
              }
            }
          },
          "required": ["tests"],
          "additionalProperties": false
        }
        """;

    public static Result<TestGenerationResult> Parse(string json) =>
        TestGenerationResultParser.Parse(json);
}
