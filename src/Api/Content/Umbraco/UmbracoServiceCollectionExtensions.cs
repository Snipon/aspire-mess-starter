using Api.Status;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Api.Content.Umbraco;

public static class UmbracoServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddUmbracoContentSource(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<UmbracoOptions>()
            .Bind(builder.Configuration.GetSection(UmbracoOptions.SectionName))
            .ValidateDataAnnotations(); // validated on first use, so build-time OpenAPI generation needs no config

        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<UmbracoOptions>>().Value;
            var secret = string.IsNullOrWhiteSpace(options.HmacSecretKey) ? null : Convert.FromBase64String(options.HmacSecretKey);
            return new DeliveryApiMapper(options.PublicBaseUrl, new ImageUrlSigner(secret), options.RenditionWidths);
        });

        builder.Services.AddHttpClient<IContentSource, UmbracoContentSource>(ConfigureDeliveryApi);

        // Its own client without the default retries: a check against a down CMS fails fast (instead of running into the
        // check timeout, which is logged as an error) and never trips the content client's circuit breaker or its Seq
        // alert. Degraded, not Unhealthy: /health (Aspire's WaitFor, the availability tests) keeps answering 200 while
        // the CMS is down, as before this check existed; the status page shows it.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental
        builder.Services.AddHttpClient<UmbracoHealthCheck>(ConfigureDeliveryApi).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        builder.Services.AddHealthChecks()
            .AddCheck<UmbracoHealthCheck>("content", HealthStatus.Degraded, [StatusReporter.DependencyTag], StatusReporter.CheckTimeout);

        return builder;
    }

    private static void ConfigureDeliveryApi(IServiceProvider sp, HttpClient http)
    {
        var options = sp.GetRequiredService<IOptions<UmbracoOptions>>().Value;
        http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(options.DeliveryApiKey))
        {
            http.DefaultRequestHeaders.Add("Api-Key", options.DeliveryApiKey);
        }
    }
}
