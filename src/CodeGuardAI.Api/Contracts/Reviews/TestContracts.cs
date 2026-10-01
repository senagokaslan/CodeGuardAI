using System.ComponentModel.DataAnnotations;

namespace CodeGuardAI.Api.Contracts.Reviews;

public sealed class CreateTestSuggestionsRequest
{
    [Required]
    [MinLength(1)]
    public Guid[] FindingIds { get; init; } = [];

    [Required]
    [StringLength(200)]
    public string? Model { get; init; }

    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 60;

    [Range(1, 100)]
    public int MaxSuggestions { get; init; } = 50;
}

public sealed class RunTestsRequest
{
    [Required]
    [StringLength(500)]
    public string? ProjectPath { get; init; }

    [Range(1, 600)]
    public int TimeoutSeconds { get; init; } = 120;
}

public sealed record TestSuggestionResponse(
    Guid Id,
    string Type,
    string Name,
    string Target,
    string Scenario,
    string Reason,
    string? SuggestedTestCode);

public sealed record TestSuggestionBatchResponse(
    Guid ReviewId,
    Guid ModelRunId,
    IReadOnlyList<TestSuggestionResponse> Tests);

public sealed record TestRunResponse(
    Guid ReviewId,
    int ExitCode,
    int PassedCount,
    int FailedCount,
    int SkippedCount,
    bool Truncated,
    string Output);
