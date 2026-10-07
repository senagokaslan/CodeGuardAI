using CodeGuardAI.Api.Contracts.Reviews;
using CodeGuardAI.Api.Errors;
using CodeGuardAI.Application.Agents;
using CodeGuardAI.Application.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace CodeGuardAI.Api.Controllers;

[ApiController]
[Route("reviews")]
public sealed class ReviewsController(
    IReviewOrchestrator orchestrator,
    ITestOrchestrator testOrchestrator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ReviewResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewResponse>> Create(
        CreateReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await orchestrator.CreateAsync(
            new CreateReviewCommand(
                request.ProjectId,
                new ReviewAgentPolicy(
                    request.Model!,
                    TimeSpan.FromSeconds(request.TimeoutSeconds),
                    request.MaxFindings)),
            cancellationToken);
        if (result.IsFailure)
        {
            return ResultErrorMapper.ToActionResult(result.Error, HttpContext);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await orchestrator.GetAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(ToResponse(result.Value))
            : ResultErrorMapper.ToActionResult(result.Error, HttpContext);
    }

    [HttpGet("/projects/{projectId:guid}/reviews")]
    [ProducesResponseType<ReviewHistoryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReviewHistoryResponse>> History(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var result = await orchestrator.GetHistoryAsync(projectId, cancellationToken);
        return result.IsSuccess
            ? Ok(new ReviewHistoryResponse(result.Value.Select(ToResponse).ToArray()))
            : ResultErrorMapper.ToActionResult(result.Error, HttpContext);
    }

    [HttpPost("/api/reviews/{id:guid}/tests")]
    [ProducesResponseType<TestSuggestionBatchResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TestSuggestionBatchResponse>> CreateTests(
        Guid id,
        CreateTestSuggestionsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await testOrchestrator.CreateSuggestionsAsync(
            new CreateTestSuggestionsCommand(
                id,
                request.FindingIds,
                new TestAgentPolicy(
                    request.Model!,
                    TimeSpan.FromSeconds(request.TimeoutSeconds),
                    request.MaxSuggestions)),
            cancellationToken);
        if (result.IsFailure)
        {
            return ResultErrorMapper.ToActionResult(result.Error, HttpContext);
        }

        var response = new TestSuggestionBatchResponse(
            result.Value.ReviewId,
            result.Value.ModelRunId,
            result.Value.Tests.Select(test => new TestSuggestionResponse(
                test.Id,
                test.Type.ToString(),
                test.Name,
                test.Target,
                test.Scenario,
                test.Reason,
                test.SuggestedTestCode)).ToArray());
        return Created($"/api/reviews/{id}/tests", response);
    }

    [HttpPost("/api/reviews/{id:guid}/test-runs")]
    [ProducesResponseType<TestRunResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TestRunResponse>> RunTests(
        Guid id,
        RunTestsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await testOrchestrator.RunAsync(
            new RunTestsCommand(
                id,
                request.ProjectPath!,
                TimeSpan.FromSeconds(request.TimeoutSeconds)),
            cancellationToken);
        if (result.IsFailure)
        {
            return ResultErrorMapper.ToActionResult(result.Error, HttpContext);
        }

        return Ok(new TestRunResponse(
            result.Value.ReviewId,
            result.Value.ExitCode,
            result.Value.PassedCount,
            result.Value.FailedCount,
            result.Value.SkippedCount,
            result.Value.Truncated,
            result.Value.Output));
    }

    private static ReviewResponse ToResponse(ReviewReadModel review)
    {
        return new ReviewResponse(
            review.Id,
            review.ProjectId,
            review.Status.ToString(),
            review.Model,
            review.PromptVersion,
            review.StartedAtUtc,
            review.CompletedAtUtc,
            review.ErrorCode,
            review.Findings.Select(finding => new ReviewFindingResponse(
                finding.Id,
                finding.FilePath,
                finding.StartLine,
                finding.EndLine,
                finding.Severity.ToString(),
                finding.Category.ToString(),
                finding.Title,
                finding.Reason,
                finding.Suggestion,
                finding.Confidence)).ToArray());
    }
}
