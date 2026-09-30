using System.ComponentModel.DataAnnotations;

namespace CodeGuardAI.Api.Contracts.Reviews;

public sealed class CreateReviewRequest
{
    public Guid ProjectId { get; init; }

    [Required]
    [StringLength(200)]
    public string? Model { get; init; }

    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 60;

    [Range(1, 500)]
    public int MaxFindings { get; init; } = 100;
}
