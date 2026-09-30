using System.Net;
using System.Text;
using System.Text.Json;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Infrastructure.LLM;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using GeminiProviderAdapter = CodeGuardAI.Infrastructure.LLM.GeminiProvider;

namespace CodeGuardAI.UnitTests.LLM.GeminiProvider;

public sealed class GeminiProviderContractTests
{
    private const string ValidReviewJson = "{\"findings\":[]}";

    [Fact]
    public async Task Success_sends_header_auth_separate_roles_and_structured_output_schema()
    {
        CapturedRequest? captured = null;
        var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            captured = await CapturedRequest.CreateAsync(request, cancellationToken);
            return JsonResponse(CreateEnvelope(ValidReviewJson));
        });
        var provider = CreateProvider(handler);

        var result = await provider.GenerateReviewAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidReviewJson, result.Response.Content);
        Assert.Equal("gemini", result.Response.Provider);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.EndsWith(
            "/v1beta/models/gemini-test:generateContent",
            captured.Uri.AbsoluteUri,
            StringComparison.Ordinal);
        Assert.DoesNotContain("test-api-key", captured.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("test-api-key", Assert.Single(captured.ApiKeyValues));

        using var body = JsonDocument.Parse(captured.Body);
        var root = body.RootElement;
        Assert.Equal(
            "System instructions.",
            root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(
            "Review repository context.",
            root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        var generationConfig = root.GetProperty("generationConfig");
        Assert.Equal("application/json", generationConfig.GetProperty("responseMimeType").GetString());
        Assert.Equal(
            "object",
            generationConfig.GetProperty("responseJsonSchema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Malformed_structured_content_is_invalid_response_without_retry()
    {
        var handler = new DelegateHandler((_, _) =>
            Task.FromResult(JsonResponse(CreateEnvelope("not-json"))));
        var provider = CreateProvider(handler);

        var result = await provider.GenerateReviewAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LLMProviderErrorType.InvalidResponse, result.Error.Type);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Request_timeout_cancels_http_and_returns_typed_timeout()
    {
        var handlerObservedCancellation = false;
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                handlerObservedCancellation = true;
                throw;
            }

            throw new InvalidOperationException("Timeout fixture unexpectedly continued.");
        });
        var provider = CreateProvider(handler);

        var result = await provider.GenerateReviewAsync(
            CreateRequest(TimeSpan.FromMilliseconds(25)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LLMProviderErrorType.Timeout, result.Error.Type);
        Assert.True(handlerObservedCancellation);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task External_cancellation_is_propagated_instead_of_mapped_to_timeout()
    {
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation fixture unexpectedly continued.");
        });
        var provider = CreateProvider(handler);
        using var cancellation = new CancellationTokenSource();

        var operation = provider.GenerateReviewAsync(CreateRequest(), cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task Rate_limit_is_retried_at_most_twice_and_returns_typed_error()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var logs = new RecordingLogger<GeminiProviderAdapter>();
        var httpLogs = new RecordingLogger<GeminiHttpClient>();
        var provider = CreateProvider(handler, logs, httpLogs);

        var result = await provider.GenerateReviewAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LLMProviderErrorType.RateLimit, result.Error.Type);
        Assert.Equal(3, handler.CallCount);
        var allLogs = string.Join("\n", logs.Messages.Concat(httpLogs.Messages));
        Assert.DoesNotContain("test-api-key", allLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("Review repository context.", allLogs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_error_is_retried_and_can_recover()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            JsonResponse(CreateEnvelope(ValidReviewJson))
        ]);
        var handler = new DelegateHandler((_, _) => Task.FromResult(responses.Dequeue()));
        var provider = CreateProvider(handler);

        var result = await provider.GenerateReviewAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Non_retryable_client_error_is_not_retried()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest)));
        var provider = CreateProvider(handler);

        var result = await provider.GenerateReviewAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LLMProviderErrorType.Unavailable, result.Error.Type);
        Assert.Equal(1, handler.CallCount);
    }

    private static GeminiProviderAdapter CreateProvider(
        HttpMessageHandler handler,
        RecordingLogger<GeminiProviderAdapter>? providerLogger = null,
        RecordingLogger<GeminiHttpClient>? httpLogger = null)
    {
        var options = Options.Create(new GeminiOptions
        {
            ApiKey = "test-api-key",
            MaxRetries = 2,
            RetryBaseDelayMilliseconds = 0,
            MaxRetryJitterMilliseconds = 0,
            MaxRetryDelayMilliseconds = 1
        });
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var geminiHttpClient = new GeminiHttpClient(
            client,
            options,
            httpLogger ?? new RecordingLogger<GeminiHttpClient>());
        return new GeminiProviderAdapter(
            geminiHttpClient,
            providerLogger ?? new RecordingLogger<GeminiProviderAdapter>());
    }

    private static LLMRequest CreateRequest(TimeSpan? timeout = null)
    {
        return new LLMRequest(
            "gemini-test",
            "review-v1",
            timeout ?? TimeSpan.FromSeconds(5),
            "System instructions.",
            "Review repository context.");
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string CreateEnvelope(string structuredContent)
    {
        return JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new { parts = new[] { new { text = structuredContent } } },
                    finishReason = "STOP"
                }
            }
        });
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return handler(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyList<string> ApiKeyValues,
        string Body)
    {
        public static async Task<CapturedRequest> CreateAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues("x-goog-api-key", out var apiKeyValues);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new CapturedRequest(
                request.Method,
                request.RequestUri!,
                apiKeyValues?.ToArray() ?? [],
                body);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
