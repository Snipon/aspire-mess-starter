using Api.Content;
using Api.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Api.Endpoints;

public static class PreviewEndpoints
{
    public static IEndpointRouteBuilder MapPreviewEndpoints(this IEndpointRouteBuilder app)
    {
        // Drafts are per staff member's request, never for a shared cache.
        var group = app.MapGroup("/api/preview").WithTags("Preview")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        group.MapGet("/content/{id}", GetPreviewById)
            .WithName("GetPreviewContentById")
            .WithSummary("Get the draft of a page by id")
            .WithDescription($"Returns the latest saved version of a page, unpublished changes included, for the website's " +
                $"staff preview. Requires the `{SharedSecret.PreviewHeader}` header (`Preview:ApiKey`); without a configured " +
                $"key the endpoint answers 404.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<Page>, NotFound<ProblemDetails>, BadRequest<ProblemDetails>, UnauthorizedHttpResult>> GetPreviewById(
        [FromHeader(Name = SharedSecret.PreviewHeader)] string? key, IConfiguration configuration, IContentSource content,
        IServiceProvider services, string id, string? culture, CancellationToken ct)
    {
        var expected = configuration["Preview:ApiKey"];
        if (string.IsNullOrEmpty(expected))
        {
            return TypedResults.NotFound(Problems.NotFound("Preview is not enabled."));
        }

        if (!SharedSecret.IsValid(key, expected))
        {
            return TypedResults.Unauthorized();
        }

        if (!RequestValidation.IsValidCulture(culture))
        {
            return TypedResults.BadRequest(Problems.BadRequest(RequestValidation.CultureError));
        }

        var page = await content.GetPreviewByIdAsync(id, culture, ct);
        return page is null
            ? TypedResults.NotFound(Problems.NotFound($"No page with id '{id}'."))
            : TypedResults.Ok(await ContentEndpoints.WithProductAsync(page, services, ct));
    }
}
