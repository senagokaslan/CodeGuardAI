using System.Text.Json;
using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Context;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Application.Reviews;
using CodeGuardAI.Domain.Projects;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.UnitTests.Fakes;
using Xunit;
using ReviewAgentService = CodeGuardAI.Application.Agents.ReviewAgent;

namespace CodeGuardAI.UnitTests.ReviewAgent;

public sealed class ReviewAgentTests
{
    [Fact]
    public async Task Grounded_response_creates_domain_finding_and_passes_context_to_provider()
    {
        var provider = ProviderWithResponse(ResponseJson(FindingJson(
            "src/Source.cs", 2, 2, "high", "security", 0.91m)));
        var input = CreateInput();

        var result = await CreateAgent(provider).RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var finding = Assert.Single(result.Value.Findings);
        Assert.Equal(input.Project.Id, result.Value.ProjectId);
        Assert.Equal(input.ReviewRunId, finding.ReviewRunId);
        Assert.Equal("src/Source.cs", finding.FilePath);
        Assert.Equal(2, finding.StartLine);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal("review-v1", result.Value.PromptVersion);
        var invocation = Assert.Single(provider.Invocations);
        Assert.Contains("[src/Source.cs:2-2] second", invocation.Request.UserPrompt);
    }

    [Fact]
    public async Task Unknown_file_is_quarantined_even_with_full_confidence()
    {
        var provider = ProviderWithResponse(ResponseJson(FindingJson(
            "src/Invented.cs", 1, 1, "critical", "security", 1m)));

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Findings);
        var rejected = Assert.Single(result.Value.RejectedFindings);
        Assert.Equal(FindingRejectionReason.UnknownFile, rejected.Reason);
        Assert.Equal("src/Invented.cs", rejected.Candidate.FilePath);
    }

    [Fact]
    public async Task Hallucinated_path_separator_is_not_corrected_to_match_manifest()
    {
        var provider = ProviderWithResponse(ResponseJson(FindingJson(
            "src\\Source.cs", 1, 1, "high", "bug", 0.99m)));

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Findings);
        var rejected = Assert.Single(result.Value.RejectedFindings);
        Assert.Equal(FindingRejectionReason.UnknownFile, rejected.Reason);
        Assert.Equal("src\\Source.cs", rejected.Candidate.FilePath);
    }

    [Fact]
    public async Task Line_outside_real_context_is_quarantined()
    {
        var provider = ProviderWithResponse(ResponseJson(FindingJson(
            "src/Source.cs", 3, 3, "medium", "bug", 0.8m)));

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Findings);
        var rejected = Assert.Single(result.Value.RejectedFindings);
        Assert.Equal(FindingRejectionReason.InvalidLineRange, rejected.Reason);
    }

    [Fact]
    public async Task Duplicate_findings_are_merged_with_a_deterministic_winner()
    {
        var lowerConfidence = FindingJson(
            "src/Source.cs", 1, 1, "critical", "bug", 0.70m, "Duplicate title", "Reason B");
        var higherConfidence = FindingJson(
            "src/Source.cs", 1, 1, "low", "bug", 0.95m, "duplicate title", "Reason A");
        var provider = ProviderWithResponse(ResponseJson(lowerConfidence, higherConfidence));

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var finding = Assert.Single(result.Value.Findings);
        Assert.Equal(0.95m, finding.Confidence);
        Assert.Equal(FindingSeverity.Low, finding.Severity);
        Assert.Equal(1, result.Value.DiscardedDuplicateCount);
    }

    [Fact]
    public async Task Malformed_provider_content_fails_before_domain_creation()
    {
        var provider = ProviderWithResponse("{\"findings\":[}");

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("review_agent.invalid_response", result.Error.Code);
    }

    [Fact]
    public async Task Provider_error_remains_typed_at_agent_boundary()
    {
        var provider = new FakeLLMProvider();
        provider.EnqueueResult(LLMProviderResult.Failure(new LLMProviderError(
            LLMProviderErrorType.RateLimit,
            "provider.rate_limit",
            "Rate limited.")));

        var result = await CreateAgent(provider).RunAsync(CreateInput(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("review_agent.provider_rate_limit", result.Error.Code);
    }

    [Fact]
    public async Task Cancellation_token_reaches_provider_through_agent()
    {
        var provider = new FakeLLMProvider();
        CancellationToken observedToken = default;
        provider.Enqueue(async (_, cancellationToken) =>
        {
            observedToken = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation fixture unexpectedly continued.");
        });
        using var cancellation = new CancellationTokenSource();

        var operation = CreateAgent(provider).RunAsync(CreateInput(), cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(cancellation.Token, observedToken);
    }

    private static ReviewAgentService CreateAgent(ILLMProvider provider)
    {
        return new ReviewAgentService(
            provider,
            new FindingGroundingValidator(),
            TimeProvider.System);
    }

    private static FakeLLMProvider ProviderWithResponse(string content)
    {
        var provider = new FakeLLMProvider();
        provider.EnqueueResult(LLMProviderResult.Success(new LLMResponse(
            "fake",
            "review-model",
            "review-v1",
            content,
            TimeSpan.Zero)));
        return provider;
    }

    private static ReviewAgentInput CreateInput()
    {
        var project = Project.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Example",
            "C:/repo",
            "C:/repo",
            new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero));
        var entry = new ScanManifestEntry(
            "src/Source.cs",
            20,
            RepositoryLanguage.CSharp,
            ScanEntryKind.File,
            ScanSkipReason.None);
        var manifest = new ScanManifest([entry], 1, 20);
        var context = new RepositoryContext(
            [
                new RepositoryContextSegment("src/Source.cs", 1, 1, "first"),
                new RepositoryContextSegment("src/Source.cs", 2, 2, "second"),
                new RepositoryContextSegment(
                    "src/Source.cs",
                    3,
                    3,
                    RepositoryContext.TruncationMarker,
                    IsTruncationMarker: true)
            ],
            [],
            5 + 6 + RepositoryContext.TruncationMarker.Length);
        return new ReviewAgentInput(
            project,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            manifest,
            context,
            new ReviewAgentPolicy("review-model", TimeSpan.FromSeconds(10)));
    }

    private static string ResponseJson(params string[] findings)
    {
        return $"{{\"findings\":[{string.Join(',', findings)}]}}";
    }

    private static string FindingJson(
        string filePath,
        int startLine,
        int endLine,
        string severity,
        string category,
        decimal confidence,
        string title = "Finding title",
        string reason = "Finding reason")
    {
        var severityJson = JsonSerializer.Serialize(severity);
        var categoryJson = JsonSerializer.Serialize(category);
        var filePathJson = JsonSerializer.Serialize(filePath);
        var titleJson = JsonSerializer.Serialize(title);
        var reasonJson = JsonSerializer.Serialize(reason);
        return $$"""
            {
              "severity":{{severityJson}},
              "category":{{categoryJson}},
              "filePath":{{filePathJson}},
              "startLine":{{startLine}},
              "endLine":{{endLine}},
              "title":{{titleJson}},
              "reason":{{reasonJson}},
              "suggestion":"Finding suggestion",
              "confidence":{{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
            }
            """;
    }
}
