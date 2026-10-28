namespace Api.Search.Meilisearch;

public static class MeilisearchServiceCollectionExtensions
{
    /// <summary>Registers the Meilisearch client from <c>ConnectionStrings:meilisearch</c> and the index adapter.</summary>
    public static IHostApplicationBuilder AddMeilisearchSearchIndex(this IHostApplicationBuilder builder)
    {
        builder.AddMeilisearchClient("meilisearch");
        builder.Services.AddSingleton<ISearchIndex, MeilisearchSearchIndex>();
        return builder;
    }
}
