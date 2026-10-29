using Api.Contracts;
using Api.Status;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Endpoints;

public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", GetStatus)
            .WithTags("Status")
            .WithName("GetStatus")
            .WithSummary("Get the status of the API's dependencies")
            .WithDescription("Overall and per-component status (`healthy`, `degraded` or `unhealthy`) for the status page. " +
                "Always 200: the status is in the body. The dependencies are checked at most every " +
                $"{StatusReporter.CacheDuration.TotalSeconds:0} seconds; in between the last report is returned. " +
                "Probes should use `/health`.");

        return app;
    }

    private static async Task<Ok<StatusReport>> GetStatus(StatusReporter reporter, CancellationToken ct) =>
        TypedResults.Ok(await reporter.GetAsync().WaitAsync(ct));
}
