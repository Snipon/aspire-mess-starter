using System.Reflection;
using Api.Search.Meilisearch;

namespace Api.Search;

public static class SearchServiceCollectionExtensions
{
    /// <summary>Registers the <see cref="ISearchIndex"/> selected by <c>Providers:Search</c>, plus the indexer.</summary>
    public static IHostApplicationBuilder AddSearchIndex(this IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["Providers:Search"];
        switch (provider)
        {
            case "Meilisearch":
                builder.AddMeilisearchSearchIndex();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown search provider '{provider}'. Set Providers:Search to one of: Meilisearch.");
        }

        builder.Services.AddTransient<SearchIndexer>();

        // Skip the startup reindex when the build-time OpenAPI generator (GetDocument.Insider) runs the app.
        if (Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
        {
            builder.Services.AddHostedService<SearchIndexInitializer>();
        }

        return builder;
    }
}
