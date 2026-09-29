using CodeGuardAI.Domain.Common;

namespace CodeGuardAI.Domain.Tests;

public sealed class TestCase
{
    private TestCase(
        Guid id,
        Guid reviewRunId,
        TestCaseType type,
        string name,
        string target,
        string scenario,
        string reason,
        string? suggestedTestCode,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ReviewRunId = reviewRunId;
        Type = type;
        Name = name;
        Target = target;
        Scenario = scenario;
        Reason = reason;
        SuggestedTestCode = string.IsNullOrWhiteSpace(suggestedTestCode) ? null : suggestedTestCode;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }
    public Guid ReviewRunId { get; }
    public TestCaseType Type { get; }
    public string Name { get; }
    public string Target { get; }
    public string Scenario { get; }
    public string Reason { get; }
    public string? SuggestedTestCode { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public static TestCase Create(
        Guid id,
        Guid reviewRunId,
        TestCaseType type,
        string name,
        string target,
        string scenario,
        string reason,
        string? suggestedTestCode,
        DateTimeOffset createdAtUtc)
    {
        return new TestCase(
            DomainGuard.NotEmpty(id, nameof(id)),
            DomainGuard.NotEmpty(reviewRunId, nameof(reviewRunId)),
            DomainGuard.DefinedEnum(type, nameof(type)),
            DomainGuard.Required(name, nameof(name)),
            DomainGuard.Required(target, nameof(target)),
            DomainGuard.Required(scenario, nameof(scenario)),
            DomainGuard.Required(reason, nameof(reason)),
            suggestedTestCode,
            DomainGuard.Utc(createdAtUtc, nameof(createdAtUtc)));
    }
}
