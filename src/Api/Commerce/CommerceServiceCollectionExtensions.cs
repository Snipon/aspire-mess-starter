using System.Reflection;
using Api.Commerce.Drupal;
using Api.Commerce.Meilisearch;

namespace Api.Commerce;

public static class CommerceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="ICommerceSource"/> selected by <c>Providers:Commerce</c> and the product index (on the
    /// search index's Meilisearch client). With <c>Providers:Commerce</c> empty or <c>None</c> the shop is off: no
    /// services, and the commerce endpoints are not mapped.
    /// </summary>
    public static IHostApplicationBuilder AddCommerce(this IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["Providers:Commerce"];
        switch (provider)
        {
            case var off when IsOff(off):
                return builder;
            case "Drupal":
                builder.AddDrupalCommerceSource();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown commerce provider '{provider}'. Set Providers:Commerce to one of: Drupal, None.");
        }

        builder.Services.AddSingleton<IProductIndex, MeilisearchProductIndex>();
        if (Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
        {
            builder.Services.AddHostedService<ProductIndexInitializer>();
        }

        return builder;
    }

    public static bool IsOff(string? provider) => string.IsNullOrEmpty(provider) || provider == "None";
}
