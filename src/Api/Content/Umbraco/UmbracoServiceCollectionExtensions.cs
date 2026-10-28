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

        builder.Services.AddHttpClient<IContentSource, UmbracoContentSource>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<UmbracoOptions>>().Value;
            http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(options.DeliveryApiKey))
            {
                http.DefaultRequestHeaders.Add("Api-Key", options.DeliveryApiKey);
            }
        });

        return builder;
    }
}
