using System.Text.Json;
using System.Text.Json.Serialization;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Reviews.Models;

namespace CodeGuardAI.Application.Reviews;

public static class CodeReviewResultParser
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static Result<CodeReviewResult> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result.Failure<CodeReviewResult>(ReviewParsingErrors.InvalidResponse);
        }

        try
        {
            var result = JsonSerializer.Deserialize<CodeReviewResult>(json, SerializerOptions);
            return result is null
                ? Result.Failure<CodeReviewResult>(ReviewParsingErrors.InvalidResponse)
                : Result.Success(result);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or NotSupportedException)
        {
            return Result.Failure<CodeReviewResult>(ReviewParsingErrors.InvalidResponse);
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
}

public static class ReviewParsingErrors
{
    public static readonly Error InvalidResponse = Error.Validation(
        "llm.invalid_response",
        "The model response does not match the required review schema.");
}
