namespace Api.Commerce;

/// <summary>
/// Fills the product index from the commerce source on startup and rebuilds it every <c>Commerce:ReindexInterval</c>
/// (default 10 minutes, <c>00:00:00</c> disables it) and whenever it is found empty. Stock and sales change with every
/// order, so checkout also re-indexes the products it sold (see CommerceEndpoints). Failures, such as Drupal still
/// installing or seeding, are logged and retried with backoff; they never stop the API.
/// </summary>
public sealed class ProductIndexInitializer(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ProductIndexInitializer> logger)
    : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultReindexInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reindexInterval = configuration.GetValue("Commerce:ReindexInterval", DefaultReindexInterval);
        var delay = InitialDelay;
        var lastReindex = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var healthy = false;
            try
            {
                using var scope = scopes.CreateScope();
                var index = scope.ServiceProvider.GetRequiredService<IProductIndex>();
                var due = lastReindex == DateTimeOffset.MinValue
                    || (reindexInterval > TimeSpan.Zero && DateTimeOffset.UtcNow - lastReindex >= reindexInterval);
                if (due || await index.CountAsync(stoppingToken) == 0)
                {
                    await index.EnsureIndexAsync(stoppingToken);
                    var source = scope.ServiceProvider.GetRequiredService<ICommerceSource>();
                    var count = await index.ReplaceAllAsync(source.GetAllProductsAsync(stoppingToken), stoppingToken);
                    if (count > 0)
                    {
                        logger.LogInformation("Indexed {Count} products", count);
                        lastReindex = DateTimeOffset.UtcNow;
                        healthy = true;
                    }
                    else
                    {
                        logger.LogInformation("Product reindex found no products; retrying in {Delay}", delay);
                    }
                }
                else
                {
                    healthy = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Product reindex failed; retrying in {Delay}", delay);
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
