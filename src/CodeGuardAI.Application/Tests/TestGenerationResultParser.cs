using System.Text.Json;
using System.Text.Json.Serialization;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Tests.Models;

namespace CodeGuardAI.Application.Tests;

public static class TestGenerationResultParser
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static Result<TestGenerationResult> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result.Failure<TestGenerationResult>(TestGenerationParsingErrors.InvalidResponse);
        }

        try
        {
            var result = JsonSerializer.Deserialize<TestGenerationResult>(json, SerializerOptions);
            return result is null
                ? Result.Failure<TestGenerationResult>(TestGenerationParsingErrors.InvalidResponse)
                : Result.Success(result);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or NotSupportedException)
        {
            return Result.Failure<TestGenerationResult>(TestGenerationParsingErrors.InvalidResponse);
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

public static class TestGenerationParsingErrors
{
    public static readonly Error InvalidResponse = Error.Validation(
        "test_generation.invalid_response",
        "The model response does not match the required test generation schema.");
}
