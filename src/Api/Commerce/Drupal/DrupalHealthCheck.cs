using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Commerce.Drupal;

/// <summary>Commerce dependency check: Drupal's unauthenticated /headless/health.</summary>
public sealed class DrupalHealthCheck(HttpClient http) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync("headless/health", ct);
            return response.IsSuccessStatusCode ? HealthCheckResult.Healthy() : new HealthCheckResult(context.Registration.FailureStatus);
        }
        catch (HttpRequestException)
        {
            // Returned rather than thrown, like UmbracoHealthCheck: no error log on every check while Drupal is down.
            return new HealthCheckResult(context.Registration.FailureStatus);
        }
    }
}
