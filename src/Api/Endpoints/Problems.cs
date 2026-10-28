using Microsoft.AspNetCore.Mvc;

namespace Api.Endpoints;

internal static class Problems
{
    public static ProblemDetails BadRequest(string detail) =>
        new() { Status = StatusCodes.Status400BadRequest, Title = "Bad request", Detail = detail };

    public static ProblemDetails NotFound(string detail) =>
        new() { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = detail };
}
