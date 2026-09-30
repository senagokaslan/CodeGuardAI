using System.Xml.Linq;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.UnitTests.Fakes;
using Xunit;

namespace CodeGuardAI.UnitTests.LLM;

public sealed class LLMContractTests
{
    [Fact]
    public void Request_exposes_model_prompt_version_and_timeout_metadata()
    {
        var request = CreateRequest();

        Assert.Equal("review-model", request.Model);
        Assert.Equal("review-v1", request.PromptVersion);
        Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);
        Assert.Equal("Return JSON.", request.SystemPrompt);
        Assert.Equal("Review this context.", request.UserPrompt);
    }

    [Theory]
    [InlineData(LLMProviderErrorType.Timeout)]
    [InlineData(LLMProviderErrorType.RateLimit)]
    [InlineData(LLMProviderErrorType.InvalidResponse)]
    [InlineData(LLMProviderErrorType.Unavailable)]
    public void Failure_preserves_provider_error_taxonomy(LLMProviderErrorType errorType)
    {
        var error = new LLMProviderError(errorType, "provider.error", "Provider request failed.");

        var result = LLMProviderResult.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Response);
    }

    [Fact]
    public async Task Fake_returns_queued_results_in_deterministic_order()
    {
        var firstResponse = new LLMResponse(
            "fake",
            "review-model",
            "review-v1",
            "{\"findings\":[]}",
            TimeSpan.FromMilliseconds(5));
        var rateLimit = new LLMProviderError(
            LLMProviderErrorType.RateLimit,
            "provider.rate_limit",
            "Provider rate limit was reached.",
            TimeSpan.FromSeconds(2));
        var provider = new FakeLLMProvider();
        provider.EnqueueResult(LLMProviderResult.Success(firstResponse));
        provider.EnqueueResult(LLMProviderResult.Failure(rateLimit));

        var first = await provider.GenerateReviewAsync(CreateRequest(), CancellationToken.None);
        var second = await provider.GenerateReviewAsync(CreateRequest(), CancellationToken.None);

        Assert.Same(firstResponse, first.Response);
        Assert.Same(rateLimit, second.Error);
        Assert.Equal(2, provider.Invocations.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GenerateReviewAsync(CreateRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_token_reaches_provider_step_and_is_propagated()
    {
        var provider = new FakeLLMProvider();
        CancellationToken observedToken = default;
        provider.Enqueue(async (_, cancellationToken) =>
        {
            observedToken = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation fixture unexpectedly continued.");
        });
        using var cancellation = new CancellationTokenSource();

        var operation = provider.GenerateReviewAsync(CreateRequest(), cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(cancellation.Token, observedToken);
        Assert.Equal(cancellation.Token, Assert.Single(provider.Invocations).CancellationToken);
    }

    [Fact]
    public async Task Pre_cancelled_call_does_not_consume_queued_fake_response()
    {
        var response = new LLMResponse(
            "fake",
            "review-model",
            "review-v1",
            "{\"findings\":[]}",
            TimeSpan.Zero);
        var provider = new FakeLLMProvider();
        provider.EnqueueResult(LLMProviderResult.Success(response));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GenerateReviewAsync(CreateRequest(), cancellation.Token));
        var result = await provider.GenerateReviewAsync(CreateRequest(), CancellationToken.None);

        Assert.Same(response, result.Response);
        Assert.Single(provider.Invocations);
    }

    [Fact]
    public void Application_project_has_no_gemini_or_provider_sdk_package_reference()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CodeGuardAI.Application",
            "CodeGuardAI.Application.csproj");
        var packageNames = XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(packageNames, package =>
            package.Contains("Gemini", StringComparison.OrdinalIgnoreCase) ||
            package.Contains("GenerativeAI", StringComparison.OrdinalIgnoreCase) ||
            package.Contains("Google.AI", StringComparison.OrdinalIgnoreCase));
    }

    private static LLMRequest CreateRequest()
    {
        return new LLMRequest(
            "review-model",
            "review-v1",
            TimeSpan.FromSeconds(30),
            "Return JSON.",
            "Review this context.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodeGuardAI.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root containing CodeGuardAI.sln was not found.");
    }
}
