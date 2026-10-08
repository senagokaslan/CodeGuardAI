using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Tests;
using Xunit;

namespace CodeGuardAI.UnitTests.Domain;

public sealed class EntityInvariantTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TestCase_WithUndefinedType_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestCase.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            (TestCaseType)999,
            "Reject empty input",
            "Project.Create",
            "An empty name is supplied.",
            "The invariant must be protected.",
            null,
            CreatedAtUtc));
    }

    [Theory]
    [InlineData(AIModelRunStatus.Succeeded, "ProviderError")]
    [InlineData(AIModelRunStatus.Failed, null)]
    public void AIModelRun_WithInconsistentErrorState_Throws(AIModelRunStatus status, string? errorType)
    {
        Assert.Throws<ArgumentException>(() => AIModelRun.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AIModelRunPurpose.Review,
            "Google",
            "gemini",
            "v1",
            10,
            100,
            50,
            status,
            errorType,
            CreatedAtUtc));
    }

    [Fact]
    public void AIModelRun_WithNegativeMetric_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AIModelRun.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AIModelRunPurpose.TestGeneration,
            "Google",
            "gemini",
            "v1",
            -1,
            100,
            50,
            AIModelRunStatus.Succeeded,
            null,
            CreatedAtUtc));
    }

    [Fact]
    public void AIModelRun_WithDocumentationPurpose_IsValid()
    {
        var modelRun = AIModelRun.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AIModelRunPurpose.Documentation,
            "Google",
            "gemini",
            "docs-v1",
            10,
            100,
            50,
            AIModelRunStatus.Succeeded,
            null,
            CreatedAtUtc);

        Assert.Equal(AIModelRunPurpose.Documentation, modelRun.Purpose);
    }

    [Theory]
    [InlineData(ToolExecutionStatus.Succeeded, "ToolError")]
    [InlineData(ToolExecutionStatus.Failed, null)]
    [InlineData(ToolExecutionStatus.TimedOut, null)]
    public void ToolExecution_WithInconsistentErrorState_Throws(
        ToolExecutionStatus status,
        string? errorType)
    {
        Assert.Throws<ArgumentException>(() => ToolExecution.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "dotnet test",
            status,
            10,
            "unit tests",
            "test result",
            errorType,
            CreatedAtUtc));
    }

    [Fact]
    public void ToolExecution_WithEmptyOptionalReviewId_Throws()
    {
        Assert.Throws<ArgumentException>(() => ToolExecution.Create(
            Guid.NewGuid(),
            Guid.Empty,
            "dotnet test",
            ToolExecutionStatus.Succeeded,
            10,
            "unit tests",
            "all passed",
            null,
            CreatedAtUtc));
    }
}
