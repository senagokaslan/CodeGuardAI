using System.Text.Json;
using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Context;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;
using CodeGuardAI.UnitTests.Fakes;
using Xunit;
using TestAgentService = CodeGuardAI.Application.Agents.TestAgent;

namespace CodeGuardAI.UnitTests.TestAgent;

public sealed class TestAgentTests
{
    [Theory]
    [InlineData(ReviewStatus.Pending)]
    [InlineData(ReviewStatus.Running)]
    [InlineData(ReviewStatus.Failed)]
    public async Task Only_completed_review_is_accepted(ReviewStatus status)
    {
        var provider = new FakeLLMProvider();
        var input = CreateInput(CreateReview(status));

        var result = await CreateAgent(provider).RunAsync(input, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("test_agent.review_not_completed", result.Error.Code);
        Assert.Empty(provider.Invocations);
    }

    [Fact]
    public async Task Valid_grounded_suggestion_creates_domain_test_case()
    {
        var provider = ProviderWithResponse(ResponseJson(TestJson(
            "edgeCase", "Rejects empty input", "src/Source.cs", "Input is empty", "Covers boundary", null)));

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var testCase = Assert.Single(result.Value.TestCases);
        Assert.Equal(TestCaseType.EdgeCase, testCase.Type);
        Assert.Equal("src/Source.cs", testCase.Target);
        Assert.Null(testCase.SuggestedTestCode);
        var invocation = Assert.Single(provider.Invocations);
        Assert.Contains("[src/Source.cs:2-2]", invocation.Request.UserPrompt);
        Assert.DoesNotContain("src/Unrelated.cs", invocation.Request.UserPrompt);
        Assert.Contains("Do not propose or perform production-code changes", invocation.Request.SystemPrompt);
    }

    [Fact]
    public async Task Target_must_exist_in_selected_findings_and_relevant_context_exactly()
    {
        var provider = ProviderWithResponse(ResponseJson(
            TestJson("unit", "Invented", "src/Invented.cs", "Scenario", "Reason", null),
            TestJson("unit", "Wrong separator", "src\\Source.cs", "Scenario", "Reason", null)));

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.TestCases);
        Assert.Equal(2, result.Value.RejectedSuggestions.Count);
        Assert.All(result.Value.RejectedSuggestions, rejected =>
            Assert.Equal(TestSuggestionRejectionReason.UnknownTarget, rejected.Reason));
    }

    [Fact]
    public async Task Duplicate_suggestions_are_merged_with_a_deterministic_winner()
    {
        var provider = ProviderWithResponse(ResponseJson(
            TestJson("regression", "Same case", "src/Source.cs", "Same scenario", "Reason B", null),
            TestJson("regression", "same case", "src/Source.cs", "same scenario", "Reason A", "[Fact] void Test() {}"),
            TestJson("unit", "Second", "src/Source.cs", "Other scenario", "Reason", null)));
        var input = CreateInput(policy: new TestAgentPolicy("test-model", TimeSpan.FromSeconds(10), 2));

        var result = await CreateAgent(provider).RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var testCase = Assert.Single(result.Value.TestCases, test => test.Name.Equals("same case", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("[Fact] void Test() {}", testCase.SuggestedTestCode);
        Assert.Equal(1, result.Value.DiscardedDuplicateCount);
    }

    [Fact]
    public async Task Suggestion_count_is_bounded_by_policy()
    {
        var provider = ProviderWithResponse(ResponseJson(
            TestJson("unit", "First", "src/Source.cs", "First scenario", "Reason", null),
            TestJson("validation", "Second", "src/Source.cs", "Second scenario", "Reason", null)));
        var input = CreateInput(policy: new TestAgentPolicy("test-model", TimeSpan.FromSeconds(10), 1));

        var result = await CreateAgent(provider).RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.TestCases);
        Assert.Contains(result.Value.RejectedSuggestions, rejected =>
            rejected.Reason == TestSuggestionRejectionReason.PolicyLimit);
    }

    [Theory]
    [InlineData("{\"tests\":[}")]
    [InlineData("{\"tests\":[{\"type\":\"unit\",\"name\":\"N\",\"target\":\"src/Source.cs\",\"scenario\":\"S\",\"reason\":\"R\",\"suggestedTestCode\":null,\"unknown\":true}]}")]
    [InlineData("{\"tests\":[{\"type\":1,\"name\":\"N\",\"target\":\"src/Source.cs\",\"scenario\":\"S\",\"reason\":\"R\",\"suggestedTestCode\":null}]}")]
    public async Task Invalid_structured_output_fails_closed(string content)
    {
        var provider = ProviderWithResponse(content);

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("test_agent.invalid_response", result.Error.Code);
    }

    [Fact]
    public async Task Suggested_test_code_may_be_omitted()
    {
        var provider = ProviderWithResponse(
            "{\"tests\":[{\"type\":\"unit\",\"name\":\"N\",\"target\":\"src/Source.cs\",\"scenario\":\"S\",\"reason\":\"R\"}]}");

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(result.Value.TestCases).SuggestedTestCode);
    }

    [Fact]
    public async Task Cancellation_token_reaches_test_provider()
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
    public async Task Agent_does_not_write_repository_source()
    {
        var root = Path.Combine(Path.GetTempPath(), $"codeguard-test-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "Source.cs");
        await File.WriteAllTextAsync(sourcePath, "original source");
        try
        {
            var provider = ProviderWithResponse(ResponseJson(TestJson(
                "unit", "Suggestion", "src/Source.cs", "Scenario", "Reason", "suggested only")));

            var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal("original source", await File.ReadAllTextAsync(sourcePath));
            Assert.Collection(
                Directory.GetFiles(root).Select(Path.GetFileName),
                fileName => Assert.Equal("Source.cs", fileName));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static TestAgentService CreateAgent(ILLMProvider provider) =>
        new(provider, TimeProvider.System);

    private static FakeLLMProvider ProviderWithResponse(string content)
    {
        var provider = new FakeLLMProvider();
        provider.EnqueueResult(LLMProviderResult.Success(new LLMResponse(
            "fake",
            "test-model",
            "test-v1",
            content,
            TimeSpan.FromMilliseconds(5))));
        return provider;
    }

    private static TestAgentInput CreateInput(
        ReviewRun? review = null,
        TestAgentPolicy? policy = null)
    {
        review ??= CreateReview(ReviewStatus.Completed);
        var finding = Finding.Create(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            review.Id,
            FindingSeverity.High,
            FindingCategory.Bug,
            "src/Source.cs",
            2,
            2,
            "Null dereference",
            "Value may be null",
            "Guard the value",
            0.9m,
            new DateTimeOffset(2026, 9, 30, 8, 1, 0, TimeSpan.Zero));
        var context = new RepositoryContext(
            [
                new RepositoryContextSegment("src/Source.cs", 2, 2, "value.Use();"),
                new RepositoryContextSegment("src/Unrelated.cs", 1, 1, "unrelated")
            ],
            [],
            21);
        return new TestAgentInput(
            review,
            [finding],
            context,
            policy ?? new TestAgentPolicy("test-model", TimeSpan.FromSeconds(10)));
    }

    private static ReviewRun CreateReview(ReviewStatus status)
    {
        var started = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
        var review = ReviewRun.Create(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
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

    private static string ResponseJson(params string[] tests) =>
        $"{{\"tests\":[{string.Join(',', tests)}]}}";

    private static string TestJson(
        string type,
        string name,
        string target,
        string scenario,
        string reason,
        string? suggestedTestCode)
    {
        return $$"""
            {
              "type":{{JsonSerializer.Serialize(type)}},
              "name":{{JsonSerializer.Serialize(name)}},
              "target":{{JsonSerializer.Serialize(target)}},
              "scenario":{{JsonSerializer.Serialize(scenario)}},
              "reason":{{JsonSerializer.Serialize(reason)}},
              "suggestedTestCode":{{JsonSerializer.Serialize(suggestedTestCode)}}
            }
            """;
    }
}
