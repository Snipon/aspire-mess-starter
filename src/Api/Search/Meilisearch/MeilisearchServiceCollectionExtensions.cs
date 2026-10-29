using Api.Status;

namespace Api.Search.Meilisearch;

public static class MeilisearchServiceCollectionExtensions
{
    /// <summary>Registers the Meilisearch client from <c>ConnectionStrings:meilisearch</c> and the index adapter.</summary>
    public static IHostApplicationBuilder AddMeilisearchSearchIndex(this IHostApplicationBuilder builder)
    {
        // The integration's own check is replaced by one registered for the status page (also part of /health).
        builder.AddMeilisearchClient("meilisearch", settings => settings.DisableHealthChecks = true);
        builder.Services.AddSingleton<ISearchIndex, MeilisearchSearchIndex>();
        builder.Services.AddHealthChecks()
            .AddCheck<MeilisearchIndexHealthCheck>("search", tags: [StatusReporter.DependencyTag], timeout: StatusReporter.CheckTimeout);
        return builder;
    }
}
