using System.ComponentModel.DataAnnotations;

namespace CodeGuardAI.Api.Contracts.Reviews;

public sealed class CreateDocumentationRequest
{
    [Required]
    [StringLength(200)]
    public string? Model { get; init; }

    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 120;
}
