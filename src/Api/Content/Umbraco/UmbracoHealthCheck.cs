using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Content.Umbraco;

/// <summary>Content dependency check: one small, authenticated Delivery API query.</summary>
public sealed class UmbracoHealthCheck(HttpClient http) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"{UmbracoContentSource.ContentApi}?take=1", ct);
            return response.IsSuccessStatusCode ? HealthCheckResult.Healthy() : new HealthCheckResult(context.Registration.FailureStatus);
        }
        catch (HttpRequestException)
        {
            // Returned rather than thrown: a thrown exception is logged as an error on every check while the CMS is down.
            return new HealthCheckResult(context.Registration.FailureStatus);
        }
    }
}
