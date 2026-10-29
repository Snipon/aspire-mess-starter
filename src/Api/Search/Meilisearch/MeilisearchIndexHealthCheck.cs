using Meilisearch;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Search.Meilisearch;

/// <summary>Search dependency check: Meilisearch's <c>/health</c>.</summary>
public sealed class MeilisearchIndexHealthCheck(MeilisearchClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct) =>
        await client.IsHealthyAsync(ct) ? HealthCheckResult.Healthy() : new HealthCheckResult(context.Registration.FailureStatus);
}
