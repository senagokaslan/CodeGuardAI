using System.ComponentModel.DataAnnotations;
using CodeGuardAI.Api.Contracts.Projects;
using CodeGuardAI.Api.Errors;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Application.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace CodeGuardAI.Api.Controllers;

[ApiController]
[Route("api/projects")]
[Route("projects")]
public sealed class ProjectsController(
    IProjectService projectService,
    IRepositoryScanner scanner) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectResponse>> Create(
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var result = await projectService.CreateAsync(
            new CreateProjectCommand(request.Name!, request.RepositoryPath!),
            cancellationToken);

        if (result.IsFailure)
        {
            return ResultErrorMapper.ToActionResult(result.Error, HttpContext);
        }

        var response = ToResponse(result.Value);
        return Created($"/api/projects/{response.Id}", response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await projectService.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(ToResponse(result.Value))
            : ResultErrorMapper.ToActionResult(result.Error, HttpContext);
    }

    [HttpGet]
    [ProducesResponseType<ProjectListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProjectListResponse>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, ProjectLimits.MaxPageSize)] int pageSize = ProjectLimits.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await projectService.ListAsync(page, pageSize, cancellationToken);
        if (result.IsFailure)
        {
            return ResultErrorMapper.ToActionResult(result.Error, HttpContext);
        }

        return Ok(new ProjectListResponse(
            result.Value.Items.Select(ToResponse).ToArray(),
            result.Value.Page,
            result.Value.PageSize));
    }

    [HttpPost("{id:guid}/scan")]
    [ProducesResponseType<ProjectScanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectScanResponse>> Scan(
        Guid id,
        CancellationToken cancellationToken)
    {
        var project = await projectService.GetByIdAsync(id, cancellationToken);
        if (project.IsFailure)
        {
            return ResultErrorMapper.ToActionResult(project.Error, HttpContext);
        }

        var scan = await scanner.ScanAsync(project.Value.RepositoryPath, cancellationToken);
        if (scan.IsFailure)
        {
            return ResultErrorMapper.ToActionResult(scan.Error, HttpContext);
        }

        return Ok(new ProjectScanResponse(
            project.Value.Id,
            scan.Value.IncludedFileCount,
            scan.Value.Entries.Count(entry => !entry.IsIncluded),
            scan.Value.IncludedBytes,
            scan.Value.Entries.Select(entry => new ProjectScanEntryResponse(
                entry.RelativePath,
                entry.SizeBytes,
                entry.Language.ToString(),
                entry.Kind.ToString(),
                entry.SkipReason.ToString(),
                entry.IsIncluded)).ToArray()));
    }

    private static ProjectResponse ToResponse(ProjectReadModel project)
    {
        return new ProjectResponse(project.Id, project.Name, project.RepositoryPath);
    }
}
