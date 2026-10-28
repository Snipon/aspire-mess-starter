namespace Api.Search;

/// <summary>
/// Ensures the index exists, runs an initial full reindex on startup, and afterwards rebuilds the index whenever it is
/// found empty (e.g. the search engine restarted on ephemeral storage). Reindexing is non-destructive (see
/// <see cref="ISearchIndex.ReplaceAllAsync"/>), so every api replica may run this concurrently. Failures (e.g. the CMS
/// is not ready yet) are logged and retried with exponential backoff; they never stop the API. An empty content source
/// is also retried, because a freshly installed CMS serves an empty query index for a while.
/// </summary>
public sealed class SearchIndexInitializer(IServiceScopeFactory scopes, ILogger<SearchIndexInitializer> logger)
    : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = InitialDelay;
        var initialDone = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            var healthy = false;
            try
            {
                using var scope = scopes.CreateScope();
                var index = scope.ServiceProvider.GetRequiredService<ISearchIndex>();
                if (!initialDone || await index.CountAsync(stoppingToken) == 0)
                {
                    if (initialDone)
                    {
                        logger.LogWarning("Search index is empty; rebuilding from the content source");
                    }

                    var indexed = await scope.ServiceProvider.GetRequiredService<SearchIndexer>().ReindexAllAsync(stoppingToken);
                    if (indexed > 0)
                    {
                        initialDone = true;
                        healthy = true;
                    }
                    else
                    {
                        // A freshly installed CMS answers before its content index is filled (Umbraco's Delivery API
                        // query index lags ~1 minute behind the first boot), so an empty result means "not ready yet".
                        logger.LogInformation("Reindex found no content; retrying in {Delay}", delay);
                    }
                }
                else
                {
                    healthy = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Reindex failed; retrying in {Delay}", delay);
            }

            try
            {
                if (healthy)
                {
                    delay = InitialDelay;
                    await Task.Delay(CheckInterval, stoppingToken);
                }
                else
                {
                    await Task.Delay(delay, stoppingToken);
                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
