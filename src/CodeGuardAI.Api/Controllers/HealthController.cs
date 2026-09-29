using CodeGuardAI.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace CodeGuardAI.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(new HealthResponse("Healthy"));
    }
}
