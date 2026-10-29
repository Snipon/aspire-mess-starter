using System.Text.Json.Serialization;

namespace Api.Contracts;

/// <summary>Health of the API's dependencies, for the status page.</summary>
/// <param name="Status">The worst component status (<c>healthy</c> when there are no components).</param>
/// <param name="CheckedAt">When the components were checked; reports are cached for a few seconds.</param>
public sealed record StatusReport(HealthState Status, DateTimeOffset CheckedAt, ComponentStatus[] Components);

/// <param name="Name">Stable id of the dependency: <c>content</c>, <c>search</c>, or another adapter's name.</param>
/// <param name="DisplayName">Human-readable name.</param>
/// <param name="DurationMs">How long the check took, in milliseconds.</param>
public sealed record ComponentStatus(string Name, string DisplayName, HealthState Status, long DurationMs);

[JsonConverter(typeof(JsonStringEnumConverter<HealthState>))]
public enum HealthState
{
    [JsonStringEnumMemberName("healthy")] Healthy,
    [JsonStringEnumMemberName("degraded")] Degraded,
    [JsonStringEnumMemberName("unhealthy")] Unhealthy,
}
