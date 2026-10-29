using Api.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Status;

/// <summary>
/// Builds the <see cref="StatusReport"/> from the health checks that the adapters register with
/// <see cref="DependencyTag"/>. A report is reused for <see cref="CacheDuration"/> and concurrent callers share one
/// evaluation, so the status page never multiplies the load on the dependencies.
/// </summary>
public sealed class StatusReporter(HealthCheckService health, TimeProvider time)
{
    /// <summary>Tag of the health checks that appear as components on the status page.</summary>
    public const string DependencyTag = "dependency";

    /// <summary>Timeout for a dependency check; short, so the status page answers quickly when a dependency hangs.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    private Task<StatusReport>? _report;
    private DateTimeOffset _expires;

    public Task<StatusReport> GetAsync()
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            if (_report is null || now >= _expires)
            {
                _report = CheckAsync();
                _expires = now + CacheDuration;
            }

            return _report;
        }
    }

    private async Task<StatusReport> CheckAsync()
    {
        // Not the caller's token: the evaluation is shared. Only names, statuses and durations leave the API; entry
        // descriptions and exceptions can carry messages, URLs or keys.
        var report = await health.CheckHealthAsync(r => r.Tags.Contains(DependencyTag), CancellationToken.None);
        var components = report.Entries
            .Select(e => new ComponentStatus(e.Key, DisplayName(e.Key), ToState(e.Value.Status), (long)e.Value.Duration.TotalMilliseconds))
            .ToArray();
        return new StatusReport(ToState(report.Status), time.GetUtcNow(), components);
    }

    private static string DisplayName(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    private static HealthState ToState(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => HealthState.Healthy,
        HealthStatus.Degraded => HealthState.Degraded,
        _ => HealthState.Unhealthy,
    };
}
