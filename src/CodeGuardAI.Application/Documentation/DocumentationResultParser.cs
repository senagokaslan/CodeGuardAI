using System.Text.Json;
using System.Text.Json.Serialization;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Documentation.Models;

namespace CodeGuardAI.Application.Documentation;

public static class DocumentationResultParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16
    };

    public static Result<LLMDocumentationReport> Parse(
        string json,
        int maxOutputCharacters,
        int maxFindingItems,
        int maxTestCaseItems)
    {
        if (maxOutputCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputCharacters));
        }

        if (maxFindingItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFindingItems));
        }

        if (maxTestCaseItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTestCaseItems));
        }

        if (string.IsNullOrWhiteSpace(json) || json.Length > maxOutputCharacters)
        {
            return Result.Failure<LLMDocumentationReport>(DocumentationParsingErrors.InvalidResponse);
        }

        try
        {
            var result = JsonSerializer.Deserialize<LLMDocumentationReport>(json, SerializerOptions);
            if (result is null ||
                result.FindingSections.Count > maxFindingItems ||
                result.TestCaseSections.Count > maxTestCaseItems)
            {
                return Result.Failure<LLMDocumentationReport>(DocumentationParsingErrors.InvalidResponse);
            }

            return Result.Success(result);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or NotSupportedException)
        {
            return Result.Failure<LLMDocumentationReport>(DocumentationParsingErrors.InvalidResponse);
        }
    }
}

public static class DocumentationParsingErrors
{
    public static readonly Error InvalidResponse = Error.Validation(
        "documentation.invalid_response",
        "The model response does not match the required documentation schema.");
}
