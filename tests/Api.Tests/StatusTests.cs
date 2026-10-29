using System.Net;
using System.Text.Json;
using Api.Content;
using Api.Content.Umbraco;
using Api.Contracts;
using Api.Endpoints;
using Api.Search;
using Api.Status;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Api.Tests;

public sealed class StatusTests
{
    private const string Secret = "boom at http://cms:8080 with key s3cr3t";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Endpoint_returns_200_with_dependency_components_and_no_failure_details()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<StatusReporter>();
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            .AddCheck("content", () => HealthCheckResult.Healthy(), [StatusReporter.DependencyTag])
            .AddCheck("search", () => throw new InvalidOperationException(Secret), [StatusReporter.DependencyTag])
            .AddCheck("queue", () => HealthCheckResult.Degraded(Secret), [StatusReporter.DependencyTag]);
        await using var app = builder.Build();
        app.MapStatusEndpoints();
        await app.StartAsync(Ct);

        using var response = await app.GetTestClient().GetAsync("/api/status", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("s3cr3t", body);
        Assert.DoesNotContain("cms:8080", body);
        Assert.DoesNotContain("boom", body);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("unhealthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            [("content", "Content", "healthy"), ("search", "Search", "unhealthy"), ("queue", "Queue", "degraded")],
            json.RootElement.GetProperty("components").EnumerateArray()
                .Select(c => (c.GetProperty("name").GetString(), c.GetProperty("displayName").GetString(), c.GetProperty("status").GetString())));
        Assert.Equal(["name", "displayName", "status", "durationMs"],
            json.RootElement.GetProperty("components")[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Report_is_cached_and_shared_by_concurrent_callers()
    {
        var gate = new TaskCompletionSource();
        var calls = 0;
        var time = new ManualTime();
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHealthChecks()
            .AddAsyncCheck("content", async () =>
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return HealthCheckResult.Healthy();
            }, [StatusReporter.DependencyTag])
            .Services.BuildServiceProvider();
        var reporter = new StatusReporter(services.GetRequiredService<HealthCheckService>(), time);

        var first = reporter.GetAsync();
        var second = reporter.GetAsync();
        gate.SetResult();
        Assert.Same(await first, await second);

        time.Now += StatusReporter.CacheDuration - TimeSpan.FromMilliseconds(1);
        Assert.Same(await first, await reporter.GetAsync());
        Assert.Equal(1, calls);

        time.Now += TimeSpan.FromMilliseconds(1);
        var refreshed = await reporter.GetAsync();
        Assert.NotSame(await first, refreshed);
        Assert.Equal(time.Now, refreshed.CheckedAt);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task No_dependency_checks_is_healthy()
    {
        using var services = new ServiceCollection().AddLogging().AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Unhealthy(), ["live"]).Services.BuildServiceProvider();

        var report = await new StatusReporter(services.GetRequiredService<HealthCheckService>(), TimeProvider.System).GetAsync();

        Assert.Equal(HealthState.Healthy, report.Status);
        Assert.Empty(report.Components);
    }

    [Fact]
    public void Adapters_register_their_dependency_checks()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Providers:Content"] = "Umbraco",
            ["Providers:Search"] = "Meilisearch",
            ["Umbraco:BaseUrl"] = "http://cms",
            ["Umbraco:PublicBaseUrl"] = "http://localhost:5000",
            ["ConnectionStrings:meilisearch"] = "Endpoint=http://localhost:7700;MasterKey=test",
        });
        builder.AddContentSource();
        builder.AddSearchIndex();
        using var host = builder.Build();

        var registrations = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        // The Meilisearch integration's own check is replaced, not duplicated. A CMS outage degrades /health
        // instead of failing it (it was not part of /health before).
        Assert.Equal(
            [("content", HealthStatus.Degraded), ("search", HealthStatus.Unhealthy)],
            registrations.Select(r => (r.Name, r.FailureStatus)));
        Assert.All(registrations, r =>
        {
            Assert.Contains(StatusReporter.DependencyTag, r.Tags);
            Assert.Equal(StatusReporter.CheckTimeout, r.Timeout);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, HealthStatus.Healthy)]
    [InlineData(HttpStatusCode.Unauthorized, HealthStatus.Degraded)]
    [InlineData(HttpStatusCode.InternalServerError, HealthStatus.Degraded)]
    [InlineData(null, HealthStatus.Degraded)] // connection failure
    public async Task Content_check_queries_the_delivery_api_with_the_key(HttpStatusCode? cmsStatus, HealthStatus expected)
    {
        var handler = new RecordingHandler(cmsStatus);
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Umbraco:BaseUrl"] = "http://cms",
            ["Umbraco:PublicBaseUrl"] = "http://localhost:5000",
            ["Umbraco:DeliveryApiKey"] = "key",
        });
        builder.AddUmbracoContentSource();
        builder.Services.AddHttpClient<UmbracoHealthCheck>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var host = builder.Build();

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);

        Assert.Equal(expected, report.Entries["content"].Status);
        Assert.Null(report.Entries["content"].Exception);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://cms/umbraco/delivery/api/v2/content?take=1", request.RequestUri!.ToString());
        Assert.Equal(["key"], request.Headers.GetValues("Api-Key"));
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Answers with <paramref name="status"/>, or fails like a refused connection when it is null.</summary>
    private sealed class RecordingHandler(HttpStatusCode? status) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return status is { } code
                ? Task.FromResult(new HttpResponseMessage(code))
                : Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused"));
        }
    }
}
