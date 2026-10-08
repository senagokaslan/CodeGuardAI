using System.Text;
using System.Text.Json;
using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Context;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Application.Prompts;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Application.Reviews;
using CodeGuardAI.Domain.Projects;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.UnitTests.Fakes;
using Xunit;
using ReviewAgentService = CodeGuardAI.Application.Agents.ReviewAgent;

namespace CodeGuardAI.UnitTests.Evals;

public sealed class ReviewBehaviorEvalTests
{
    public static TheoryData<string, FindingCategory?, int?> Cases => new()
    {
        { "NullDereference.cs", FindingCategory.Bug, 7 },
        { "MissingValidation.cs", FindingCategory.Reliability, 9 },
        { "PathTraversal.cs", FindingCategory.Security, 8 },
        { "ResourceDisposal.cs", FindingCategory.Reliability, 7 },
        { "CleanControl.cs", null, null }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "Eval")]
    public async Task Fixed_fixture_result_matches_semantic_expectation(
        string fixtureName,
        FindingCategory? expectedCategory,
        int? expectedLine)
    {
        var fixture = LoadFixture(fixtureName);
        var provider = CreateProvider(fixtureName, expectedCategory, expectedLine);
        var input = CreateInput(fixtureName, fixture);
        var agent = new ReviewAgentService(
            provider,
            new FindingGroundingValidator(),
            TimeProvider.System);

        var result = await agent.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.RejectedFindings);
        Assert.Single(provider.Invocations);
        if (expectedCategory is null)
        {
            Assert.Empty(result.Value.Findings);
            return;
        }

        var finding = Assert.Single(result.Value.Findings);
        Assert.Equal(expectedCategory.Value, finding.Category);
        Assert.Equal(fixtureName, finding.FilePath);
        Assert.Equal(expectedLine, finding.StartLine);
        Assert.Equal(expectedLine, finding.EndLine);
        Assert.InRange(finding.StartLine, 1, fixture.Lines.Length);
        Assert.Contains(
            input.RepositoryContext.Segments,
            segment => segment.RelativePath == finding.FilePath &&
                       segment.StartLine <= finding.StartLine &&
                       segment.EndLine >= finding.EndLine);
    }

    private static FixtureSource LoadFixture(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Evals", fixtureName);
        var content = File.ReadAllText(path);
        return new FixtureSource(content, File.ReadAllLines(path));
    }

    private static FakeLLMProvider CreateProvider(
        string fixtureName,
        FindingCategory? expectedCategory,
        int? expectedLine)
    {
        var content = expectedCategory is null
            ? "{\"findings\":[]}"
            : JsonSerializer.Serialize(new
            {
                findings = new[]
                {
                    new
                    {
                        severity = "high",
                        category = expectedCategory.Value.ToString().ToLowerInvariant(),
                        filePath = fixtureName,
                        startLine = expectedLine,
                        endLine = expectedLine,
                        title = "Semantic fixture finding",
                        reason = "Fixture-specific behavior requires review.",
                        suggestion = "Apply the fixture-appropriate guard.",
                        confidence = 0.95m
                    }
                }
            });
        var provider = new FakeLLMProvider();
        provider.EnqueueResult(LLMProviderResult.Success(new LLMResponse(
            "fake",
            "offline-eval-model",
            ReviewPromptRegistry.CurrentVersion,
            content,
            TimeSpan.FromMilliseconds(12))));
        return provider;
    }

    private static ReviewAgentInput CreateInput(string fixtureName, FixtureSource fixture)
    {
        var project = Project.Create(
            Guid.Parse("86000000-0000-0000-0000-000000000001"),
            "Offline Eval",
            "C:/offline-eval",
            "C:/offline-eval",
            new DateTimeOffset(2026, 10, 7, 18, 0, 0, TimeSpan.Zero));
        var byteCount = Encoding.UTF8.GetByteCount(fixture.Content);
        var manifest = new ScanManifest(
            [new ScanManifestEntry(
                fixtureName,
                byteCount,
                RepositoryLanguage.CSharp,
                ScanEntryKind.File,
                ScanSkipReason.None)],
            1,
            byteCount);
        var segments = fixture.Lines
            .Select((line, index) => new RepositoryContextSegment(
                fixtureName,
                index + 1,
                index + 1,
                line))
            .ToArray();
        var context = new RepositoryContext(
            segments,
            [],
            fixture.Lines.Sum(line => line.Length));
        return new ReviewAgentInput(
            project,
            Guid.Parse("86000000-0000-0000-0000-000000000002"),
            manifest,
            context,
            new ReviewAgentPolicy("offline-eval-model", TimeSpan.FromSeconds(5)));
    }

    private sealed record FixtureSource(string Content, string[] Lines);
}
