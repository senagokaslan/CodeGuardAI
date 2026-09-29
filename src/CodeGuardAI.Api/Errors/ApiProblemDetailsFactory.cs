using System.Diagnostics;
using System.Text.Json;
using CodeGuardAI.Api.Contracts.Common;
using Microsoft.AspNetCore.Mvc;

namespace CodeGuardAI.Api.Errors;

public static class ApiProblemDetailsFactory
{
    public static IActionResult CreateValidationResponse(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(
                entry => ToJsonPropertyName(entry.Key),
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The request value is invalid."
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal);

        var problemDetails = new ValidationProblemDetails(errors)
        {
            Type = "urn:codeguard:error:validation.failed",
            Title = "Request validation failed.",
            Status = StatusCodes.Status400BadRequest,
            Detail = "One or more request fields are invalid.",
            Instance = context.HttpContext.Request.Path
        };

        AddCommonExtensions(
            problemDetails,
            ApiErrorCodes.ValidationFailed,
            context.HttpContext);

        return new BadRequestObjectResult(problemDetails)
        {
            ContentTypes = { "application/problem+json" }
        };
    }

    internal static void AddCommonExtensions(
        ProblemDetails problemDetails,
        string code,
        HttpContext httpContext)
    {
        problemDetails.Extensions["code"] = code;
        problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
    }

    private static string ToJsonPropertyName(string modelStateKey)
    {
        return string.IsNullOrEmpty(modelStateKey)
            ? modelStateKey
            : JsonNamingPolicy.CamelCase.ConvertName(modelStateKey);
    }
}
