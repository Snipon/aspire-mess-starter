using Api.Content.Umbraco;

namespace Api.Content;

public static class ContentServiceCollectionExtensions
{
    /// <summary>Registers the <see cref="IContentSource"/> selected by <c>Providers:Content</c>.</summary>
    public static IHostApplicationBuilder AddContentSource(this IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["Providers:Content"];
        switch (provider)
        {
            case "Umbraco":
                builder.AddUmbracoContentSource();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown content provider '{provider}'. Set Providers:Content to one of: Umbraco.");
        }

        return builder;
    }
}
