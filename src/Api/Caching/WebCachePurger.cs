namespace Api.Caching;

/// <summary>
/// Invalidates every page in the reverse proxy cache in front of the web (<c>WebCache:PurgeUrl</c>, POST with the
/// <see cref="SecretHeader"/> header = <c>WebCache:PurgeSecret</c>; infra/web-cache). Best effort: without
/// configuration (no cache in front of the web) it does nothing, and a failed purge is only logged, because the pages
/// expire on their own within their <c>s-maxage</c>.
/// </summary>
public sealed class WebCachePurger(HttpClient http, IConfiguration configuration, ILogger<WebCachePurger> logger)
{
    public const string SecretHeader = "X-Purge-Secret";

    public async Task PurgeAsync(CancellationToken ct)
    {
        var url = configuration["WebCache:PurgeUrl"];
        var secret = configuration["WebCache:PurgeSecret"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add(SecretHeader, secret);
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Purged the web cache");
            }
            else
            {
                logger.LogWarning("Web cache purge returned {Status}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Web cache purge failed; cached pages expire on their own");
        }
    }
}
