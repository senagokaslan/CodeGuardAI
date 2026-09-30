using CodeGuardAI.Domain.Tests;

namespace CodeGuardAI.Application.Tests.Models;

public sealed record TestGenerationResult
{
    public TestGenerationResult(IReadOnlyList<LLMTestCase> tests)
    {
        ArgumentNullException.ThrowIfNull(tests);
        if (tests.Any(static test => test is null))
        {
            throw new ArgumentException("Test suggestions cannot contain null entries.", nameof(tests));
        }

        Tests = tests;
    }

    public IReadOnlyList<LLMTestCase> Tests { get; }
}

public sealed record LLMTestCase
{
    public LLMTestCase(
        TestCaseType type,
        string name,
        string target,
        string scenario,
        string reason,
        string? suggestedTestCode = null)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Path.IsPathRooted(target) ||
            target.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("The target must be a repository-relative manifest path.", nameof(target));
        }

        Type = type;
        Name = name.Trim();
        Target = target;
        Scenario = scenario.Trim();
        Reason = reason.Trim();
        SuggestedTestCode = string.IsNullOrWhiteSpace(suggestedTestCode)
            ? null
            : suggestedTestCode;
    }

    public TestCaseType Type { get; }

    public string Name { get; }

    public string Target { get; }

    public string Scenario { get; }

    public string Reason { get; }

    public string? SuggestedTestCode { get; }
}
