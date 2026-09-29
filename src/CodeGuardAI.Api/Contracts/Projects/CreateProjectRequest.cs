using System.ComponentModel.DataAnnotations;

namespace CodeGuardAI.Api.Contracts.Projects;

public sealed class CreateProjectRequest
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(
        ProjectContractLimits.NameMaxLength,
        MinimumLength = 1,
        ErrorMessage = "Name must be between 1 and 200 characters.")]
    public string? Name { get; init; }

    [Required(ErrorMessage = "Repository path is required.")]
    [StringLength(
        ProjectContractLimits.RepositoryPathMaxLength,
        MinimumLength = 1,
        ErrorMessage = "Repository path must be between 1 and 4096 characters.")]
    public string? RepositoryPath { get; init; }
}
