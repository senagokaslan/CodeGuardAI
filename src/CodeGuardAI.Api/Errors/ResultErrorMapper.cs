using CodeGuardAI.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace CodeGuardAI.Api.Errors;

public static class ResultErrorMapper
{
    public static ObjectResult ToActionResult(Error error, HttpContext httpContext)
    {
        var (status, title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Request validation failed."),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "The requested resource was not found."),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "The request conflicts with current state."),
            ErrorType.Failure => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Type, "Unsupported error type.")
        };

        var problemDetails = new ProblemDetails
        {
            Type = $"urn:codeguard:error:{error.Code}",
            Title = title,
            Status = status,
            Detail = error.Type == ErrorType.Failure
                ? "The server could not complete the request."
                : error.Description,
            Instance = httpContext.Request.Path
        };

        ApiProblemDetailsFactory.AddCommonExtensions(problemDetails, error.Code, httpContext);

        return new ObjectResult(problemDetails)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }
}
