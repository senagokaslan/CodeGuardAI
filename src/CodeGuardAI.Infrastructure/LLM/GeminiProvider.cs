using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Infrastructure.LLM.GeminiSchemas;
using Microsoft.Extensions.Logging;

namespace CodeGuardAI.Infrastructure.LLM;

public sealed class GeminiProvider(
    GeminiHttpClient httpClient,
    ILogger<GeminiProvider> logger) : ILLMProvider
{
    private const int MaxResponseBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Name => "gemini";

    public async Task<LLMProviderResult> GenerateReviewAsync(
        LLMRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var requestJson = CreateRequestJson(request);
            using var response = await httpClient.GenerateContentAsync(
                request.Model,
                requestJson,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var error = CreateHttpError(response);
                LogFailure(error.Type, response.StatusCode);
                return LLMProviderResult.Failure(error);
            }

            var content = await ReadBoundedContentAsync(response.Content, timeout.Token);
            var structuredContent = ExtractStructuredContent(content);
            if (structuredContent is null || GeminiReviewSchema.Parse(structuredContent).IsFailure)
            {
                LogFailure(LLMProviderErrorType.InvalidResponse, response.StatusCode);
                return InvalidResponse();
            }

            return LLMProviderResult.Success(new LLMResponse(
                "gemini",
                request.Model,
                request.PromptVersion,
                structuredContent,
                stopwatch.Elapsed));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(LLMProviderErrorType.Timeout, null);
            return LLMProviderResult.Failure(new LLMProviderError(
                LLMProviderErrorType.Timeout,
                "gemini.timeout",
                "The model request exceeded its timeout."));
        }
        catch (HttpRequestException)
        {
            LogFailure(LLMProviderErrorType.Unavailable, null);
            return LLMProviderResult.Failure(new LLMProviderError(
                LLMProviderErrorType.Unavailable,
                "gemini.unavailable",
                "The model provider is unavailable."));
        }
        catch (JsonException)
        {
            LogFailure(LLMProviderErrorType.InvalidResponse, null);
            return InvalidResponse();
        }
    }

    private static string CreateRequestJson(LLMRequest request)
    {
        using var schema = JsonDocument.Parse(GeminiReviewSchema.Definition);
        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = request.SystemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = request.UserPrompt } }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseJsonSchema = schema.RootElement
            }
        };
        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static async Task<string> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await content.LoadIntoBufferAsync(MaxResponseBytes, cancellationToken);
        return await content.ReadAsStringAsync(cancellationToken);
    }

    private static string? ExtractStructuredContent(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() != 1)
        {
            return null;
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("finishReason", out var finishReason) ||
            finishReason.GetString() != "STOP" ||
            !candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array ||
            parts.GetArrayLength() != 1 ||
            !parts[0].TryGetProperty("text", out var text) ||
            text.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return text.GetString();
    }

    private static LLMProviderError CreateHttpError(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            return new LLMProviderError(
                LLMProviderErrorType.RateLimit,
                "gemini.rate_limit",
                "The model provider rate limit was reached.",
                retryAfter > TimeSpan.Zero ? retryAfter : null);
        }

        return new LLMProviderError(
            LLMProviderErrorType.Unavailable,
            "gemini.unavailable",
            "The model provider is unavailable.");
    }

    private static LLMProviderResult InvalidResponse()
    {
        return LLMProviderResult.Failure(new LLMProviderError(
            LLMProviderErrorType.InvalidResponse,
            "gemini.invalid_response",
            "The model provider returned an invalid response."));
    }

    private void LogFailure(LLMProviderErrorType errorType, HttpStatusCode? statusCode)
    {
        logger.LogWarning(
            "Gemini request failed with category {ErrorType} and HTTP status {StatusCode}.",
            errorType,
            statusCode is null ? null : (int)statusCode);
    }
}
