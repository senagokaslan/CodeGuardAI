using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeGuardAI.Infrastructure.LLM;

public sealed class GeminiHttpClient(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiHttpClient> logger)
{
    private const string ApiKeyHeaderName = "x-goog-api-key";
    private readonly GeminiOptions _options = options.Value;

    public async Task<HttpResponseMessage> GenerateContentAsync(
        string model,
        string requestJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestJson);
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Gemini API credentials are unavailable.");
        }

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = CreateRequest(model, requestJson, _options.ApiKey);
            var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var maxRetries = Math.Clamp(_options.MaxRetries, 0, 2);
            if (!ShouldRetry(response.StatusCode) || attempt >= maxRetries)
            {
                return response;
            }

            logger.LogWarning(
                "Gemini request received retryable HTTP status {StatusCode} on attempt {Attempt}.",
                (int)response.StatusCode,
                attempt + 1);
            var delay = GetRetryDelay(response, attempt);
            response.Dispose();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static HttpRequestMessage CreateRequest(
        string model,
        string requestJson,
        string apiKey)
    {
        var escapedModel = Uri.EscapeDataString(model.Trim());
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{escapedModel}:generateContent");
        request.Headers.Add(ApiKeyHeaderName, apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
        return request;
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        var baseDelayMilliseconds = Math.Max(0, _options.RetryBaseDelayMilliseconds);
        var exponentialMilliseconds = Math.Min(
            (long)baseDelayMilliseconds * (1 << attempt),
            int.MaxValue);
        var jitterMilliseconds = _options.MaxRetryJitterMilliseconds > 0
            ? Random.Shared.Next(0, _options.MaxRetryJitterMilliseconds + 1)
            : 0;
        var calculated = retryAfter ?? TimeSpan.FromMilliseconds(
            exponentialMilliseconds + jitterMilliseconds);
        var maxDelay = TimeSpan.FromMilliseconds(Math.Max(0, _options.MaxRetryDelayMilliseconds));
        return calculated > maxDelay
            ? maxDelay
            : calculated;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
    }
}
