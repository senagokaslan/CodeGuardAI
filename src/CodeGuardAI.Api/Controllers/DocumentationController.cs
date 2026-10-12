using CodeGuardAI.Api.Contracts.Reviews;
using CodeGuardAI.Api.Errors;
using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace CodeGuardAI.Api.Controllers;

[ApiController]
[Route("api/reviews/{reviewId:guid}/documentation")]
public sealed class DocumentationController(IDocsOrchestrator orchestrator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<DocumentationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DocumentationResponse>> Create(
        Guid reviewId,
        CreateDocumentationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await orchestrator.CreateAsync(
            new CreateDocumentationCommand(
                reviewId,
                new DocsAgentPolicy(
                    request.Model!,
                    TimeSpan.FromSeconds(request.TimeoutSeconds))),
            cancellationToken);
        if (result.IsFailure)
        {
            return ResultErrorMapper.ToActionResult(result.Error, HttpContext);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(Get), new { reviewId }, response);
    }

    [HttpGet]
    [ProducesResponseType<DocumentationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentationResponse>> Get(
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        var result = await orchestrator.GetAsync(reviewId, cancellationToken);
        return result.IsSuccess
            ? Ok(ToResponse(result.Value))
            : ResultErrorMapper.ToActionResult(result.Error, HttpContext);
    }

    private static DocumentationResponse ToResponse(DocumentationReadModel report) =>
        new(
            report.Id,
            report.ReviewId,
            report.ModelRunId,
            report.Title,
            report.MarkdownContent,
            report.CreatedAtUtc);
}
