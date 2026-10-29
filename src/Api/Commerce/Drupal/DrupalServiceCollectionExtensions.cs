using Api.Status;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Api.Commerce.Drupal;

public static class DrupalServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddDrupalCommerceSource(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<DrupalOptions>()
            .Bind(builder.Configuration.GetSection(DrupalOptions.SectionName))
            .ValidateDataAnnotations(); // validated on first use, so build-time OpenAPI generation needs no config

        // Cart changes and checkout are not idempotent: retry only GETs (a retried "add item" would add twice).
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental
        builder.Services.AddHttpClient<ICommerceSource, DrupalCommerceSource>(Configure)
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());

        // Degraded, not Unhealthy, like the content check: the status page shows it, /health stays 200.
        builder.Services.AddHttpClient<DrupalHealthCheck>(Configure).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        builder.Services.AddHealthChecks()
            .AddCheck<DrupalHealthCheck>("commerce", HealthStatus.Degraded, [StatusReporter.DependencyTag], StatusReporter.CheckTimeout);
        return builder;
    }

    private static void Configure(IServiceProvider sp, HttpClient http)
    {
        var options = sp.GetRequiredService<IOptions<DrupalOptions>>().Value;
        http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
        }
    }
}
