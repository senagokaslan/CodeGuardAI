using System.Text.Json;
using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Documentation;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.UnitTests.Fakes;
using Xunit;

namespace CodeGuardAI.UnitTests.Documentation;

public sealed class DocsAgentTests
{
    [Theory]
    [InlineData(ReviewStatus.Pending)]
    [InlineData(ReviewStatus.Running)]
    [InlineData(ReviewStatus.Failed)]
    public async Task Only_completed_review_is_accepted(ReviewStatus status)
    {
        var provider = new FakeLLMProvider();

        var result = await CreateAgent(provider).RunAsync(
            CreateInput(CreateReview(status)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("docs_agent.review_not_completed", result.Error.Code);
        Assert.Empty(provider.Invocations);
    }

    [Fact]
    public async Task Completed_review_without_test_suggestions_produces_markdown()
    {
        var input = CreateInput();
        var finding = Assert.Single(input.Findings);
        var provider = ProviderWithResponse(ResponseJson(
            [new { findingId = finding.Id, impact = "May expose invalid state" }],
            []));

        var result = await CreateAgent(provider).RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(input.ReviewRun.Id, result.Value.ReviewRunId);
        Assert.Equal("docs-v1", result.Value.PromptVersion);
        Assert.Contains("Severity: High", result.Value.MarkdownContent);
        Assert.Contains("No persisted test suggestions", result.Value.MarkdownContent);
    }

    [Fact]
    public async Task Prompt_and_report_exclude_suggested_test_source()
    {
        const string sourceSentinel = "SECRET_SOURCE_CODE_SHOULD_NOT_APPEAR";
        var review = CreateReview(ReviewStatus.Completed);
        var finding = CreateFinding(review.Id);
        var testCase = DocumentationGroundingValidatorTests.CreateTestCase(sourceSentinel);
        var input = new DocsAgentInput(
            review,
            [finding],
            [testCase],
            new DocsAgentPolicy("docs-model", TimeSpan.FromSeconds(10)));
        var provider = ProviderWithResponse(ResponseJson(
            [new { findingId = finding.Id, impact = "Impact" }],
            [new { testCaseId = testCase.Id, coverage = "Coverage" }]));

        var result = await CreateAgent(provider).RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var invocation = Assert.Single(provider.Invocations);
        Assert.DoesNotContain(sourceSentinel, invocation.Request.UserPrompt);
        Assert.DoesNotContain(sourceSentinel, result.Value.MarkdownContent);
        Assert.DoesNotContain("repositoryPath", invocation.Request.UserPrompt);
    }

    [Fact]
    public async Task Cancellation_token_reaches_documentation_provider()
    {
        var provider = new FakeLLMProvider();
        CancellationToken observed = default;
        provider.Enqueue(async (_, cancellationToken) =>
        {
            observed = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation fixture unexpectedly continued.");
        });
        using var cancellation = new CancellationTokenSource();

        var operation = CreateAgent(provider).RunAsync(CreateInput(), cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(cancellation.Token, observed);
    }

    [Fact]
    public async Task Raw_prompt_echo_is_rejected_before_rendering()
    {
        var input = CreateInput();
        var finding = Assert.Single(input.Findings);
        var provider = new FakeLLMProvider();
        provider.Enqueue((request, _) => Task.FromResult(LLMProviderResult.Success(new LLMResponse(
            "fake",
            "docs-model",
            "docs-v1",
            JsonSerializer.Serialize(new
            {
                title = "Technical report",
                executiveSummary = request.SystemPrompt,
                findingSections = new[] { new { findingId = finding.Id, impact = "Impact" } },
                testCaseSections = Array.Empty<object>(),
                conclusion = "Conclusion"
            }),
            TimeSpan.FromMilliseconds(5)))));

        var result = await CreateAgent(provider).RunAsync(input, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("docs_agent.invalid_response", result.Error.Code);
    }

    [Fact]
    public async Task Persisted_item_limit_is_enforced_before_provider_call()
    {
        var review = CreateReview(ReviewStatus.Completed);
        var input = new DocsAgentInput(
            review,
            [CreateFinding(review.Id)],
            [],
            new DocsAgentPolicy("docs-model", TimeSpan.FromSeconds(10), maxFindings: 0));
        var provider = new FakeLLMProvider();

        var result = await CreateAgent(provider).RunAsync(input, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("docs_agent.input_limit_exceeded", result.Error.Code);
        Assert.Empty(provider.Invocations);
    }

    private static DocsAgent CreateAgent(ILLMProvider provider) =>
        new(provider, new DocumentationGroundingValidator(), new MarkdownReportRenderer());

    private static FakeLLMProvider ProviderWithResponse(string content)
    {
        var provider = new FakeLLMProvider();
        provider.EnqueueResult(LLMProviderResult.Success(new LLMResponse(
            "fake",
            "docs-model",
            "docs-v1",
            content,
            TimeSpan.FromMilliseconds(5))));
        return provider;
    }

    private static DocsAgentInput CreateInput(ReviewRun? review = null)
    {
        review ??= CreateReview(ReviewStatus.Completed);
        return new DocsAgentInput(
            review,
            [CreateFinding(review.Id)],
            [],
            new DocsAgentPolicy("docs-model", TimeSpan.FromSeconds(10)));
    }

    private static Finding CreateFinding(Guid reviewId) => Finding.Create(
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        reviewId,
        FindingSeverity.High,
        FindingCategory.Security,
        "src/Service.cs",
        10,
        12,
        "Unsafe input",
        "Input is not validated",
        "Validate before use",
        0.9m,
        new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));

    private static ReviewRun CreateReview(ReviewStatus status)
    {
        var started = new DateTimeOffset(2026, 10, 9, 7, 0, 0, TimeSpan.Zero);
        var review = ReviewRun.Create(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "review-model",
            "review-v1",
            started);
        if (status != ReviewStatus.Pending)
        {
            review.TryStart();
        }

        if (status == ReviewStatus.Completed)
        {
            review.TryComplete(started.AddMinutes(1));
        }
        else if (status == ReviewStatus.Failed)
        {
            review.TryFail("ReviewFailed", started.AddMinutes(1));
        }

        return review;
    }

    private static string ResponseJson(object[] findingSections, object[] testCaseSections) =>
        JsonSerializer.Serialize(new
        {
            title = "Technical report",
            executiveSummary = "Summary",
            findingSections,
            testCaseSections,
            conclusion = "Conclusion"
        });
}
