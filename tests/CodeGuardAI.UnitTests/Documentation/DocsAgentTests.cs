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
    public async Task Completed_review_without_test_suggestions_produces_grounded_report()
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
        Assert.Equal("Technical report", result.Value.Report.Title);
        Assert.Empty(result.Value.Report.TestCases);
        Assert.Equal(LLMOperation.Documentation, Assert.Single(provider.Invocations).Operation);
    }

    [Fact]
    public async Task Completed_review_with_findings_and_tests_produces_fully_grounded_report()
    {
        var review = CreateReview(ReviewStatus.Completed);
        var finding = CreateFinding(review.Id);
        var testCase = DocumentationGroundingValidatorTests.CreateTestCase();
        var input = new DocsAgentInput(
            review,
            "Example project",
            [finding],
            [testCase],
            new DocsAgentPolicy("docs-model", TimeSpan.FromSeconds(10)));
        var provider = ProviderWithResponse(ResponseJson(
            [new { findingId = finding.Id, impact = "Finding impact" }],
            [new { testCaseId = testCase.Id, coverage = "Test coverage" }]));

        var result = await CreateAgent(provider).RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(finding, Assert.Single(result.Value.Report.Findings).Finding);
        Assert.Same(testCase, Assert.Single(result.Value.Report.TestCases).TestCase);
        Assert.Equal(1, result.Value.FindingCount);
        Assert.Equal(1, result.Value.TestCaseCount);
    }

    [Theory]
    [InlineData("unknown-finding", "documentation.unknown_finding")]
    [InlineData("duplicate-finding", "documentation.duplicate_finding")]
    [InlineData("unknown-test", "documentation.unknown_test_case")]
    [InlineData("duplicate-test", "documentation.duplicate_test_case")]
    public async Task Unknown_or_duplicate_identifiers_fail_closed(
        string scenario,
        string expectedCode)
    {
        var review = CreateReview(ReviewStatus.Completed);
        var finding = CreateFinding(review.Id);
        var testCase = DocumentationGroundingValidatorTests.CreateTestCase();
        var findingId = scenario == "unknown-finding" ? Guid.NewGuid() : finding.Id;
        var testCaseId = scenario == "unknown-test" ? Guid.NewGuid() : testCase.Id;
        var findingSections = scenario == "duplicate-finding"
            ? new object[]
            {
                new { findingId, impact = "First" },
                new { findingId, impact = "Second" }
            }
            : [new { findingId, impact = "Impact" }];
        var testSections = scenario == "duplicate-test"
            ? new object[]
            {
                new { testCaseId, coverage = "First" },
                new { testCaseId, coverage = "Second" }
            }
            : [new { testCaseId, coverage = "Coverage" }];
        var provider = ProviderWithResponse(ResponseJson(findingSections, testSections));
        var input = new DocsAgentInput(
            review,
            "Example project",
            [finding],
            [testCase],
            new DocsAgentPolicy("docs-model", TimeSpan.FromSeconds(10)));

        var result = await CreateAgent(provider).RunAsync(input, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"title\":\"Incomplete\"}")]
    public async Task Malformed_provider_json_fails_closed(string content)
    {
        var provider = ProviderWithResponse(content);

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("docs_agent.invalid_response", result.Error.Code);
    }

    [Theory]
    [InlineData(LLMProviderErrorType.Timeout, "docs_agent.provider_timeout")]
    [InlineData(LLMProviderErrorType.RateLimit, "docs_agent.provider_rate_limit")]
    [InlineData(LLMProviderErrorType.Unavailable, "docs_agent.provider_unavailable")]
    [InlineData(LLMProviderErrorType.InvalidResponse, "docs_agent.provider_invalid_response")]
    public async Task Provider_failures_remain_typed_at_agent_boundary(
        LLMProviderErrorType providerErrorType,
        string expectedCode)
    {
        var provider = new FakeLLMProvider();
        provider.EnqueueResult(LLMProviderResult.Failure(new LLMProviderError(
            providerErrorType,
            "provider.failure",
            "Provider failed.")));

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public async Task Prompt_excludes_suggested_test_source()
    {
        const string sourceSentinel = "SECRET_SOURCE_CODE_SHOULD_NOT_APPEAR";
        var review = CreateReview(ReviewStatus.Completed);
        var finding = CreateFinding(review.Id);
        var testCase = DocumentationGroundingValidatorTests.CreateTestCase(sourceSentinel);
        var input = new DocsAgentInput(
            review,
            "Example project",
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
            "Example project",
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
        new(provider, new DocumentationGroundingValidator());

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
            "Example project",
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
