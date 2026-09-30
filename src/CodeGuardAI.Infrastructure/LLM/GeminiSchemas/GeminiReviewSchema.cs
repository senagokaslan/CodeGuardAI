using System.Text.Json;
using System.Text.Json.Serialization;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Reviews.Models;

namespace CodeGuardAI.Infrastructure.LLM.GeminiSchemas;

public static class GeminiReviewSchema
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

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
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result.Failure<CodeReviewResult>(GeminiSchemaErrors.InvalidResponse);
        }

        try
        {
            var result = JsonSerializer.Deserialize<CodeReviewResult>(json, SerializerOptions);
            return result is null
                ? Result.Failure<CodeReviewResult>(GeminiSchemaErrors.InvalidResponse)
                : Result.Success(result);
        }
        catch (Exception exception) when (IsInvalidResponse(exception))
        {
            return Result.Failure<CodeReviewResult>(GeminiSchemaErrors.InvalidResponse);
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }

    private static bool IsInvalidResponse(Exception exception)
    {
        return exception is JsonException or ArgumentException or NotSupportedException;
    }
}

public static class GeminiSchemaErrors
{
    public static readonly Error InvalidResponse = Error.Validation(
        "llm.invalid_response",
        "The model response does not match the required review schema.");
}
