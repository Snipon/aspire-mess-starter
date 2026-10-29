using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Cms.Headless;

/// <summary>
/// Sends content ids straight to the API's content webhook endpoint (<see cref="ContentWebhookOptions.TargetUrl"/>),
/// in the background and in batches, so content created in bulk (the demo seed) reaches the search index without going
/// through Umbraco's webhook queue, which does not reliably fire for content published during startup. The API re-reads
/// each id from the published cache, so this can run right after the transaction commits.
/// </summary>
internal sealed class SearchIndexNotifier(
    IHttpClientFactory httpClientFactory,
    IOptions<ContentWebhookOptions> options,
    ILogger<SearchIndexNotifier> logger)
{
    private const int BatchSize = 100;
    private const int MaxAttempts = 8;
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);

    public void NotifyInBackground(IReadOnlyList<Guid> keys)
    {
        ContentWebhookOptions settings = options.Value;
        if (keys.Count == 0 || string.IsNullOrWhiteSpace(settings.TargetUrl) || string.IsNullOrWhiteSpace(settings.Secret))
        {
            return;
        }

        // Fire and forget: the API may still be starting (it then indexes everything on its own startup anyway).
        _ = Task.Run(() => NotifyAsync(settings.TargetUrl, settings.Secret, keys));
    }

    private async Task NotifyAsync(string url, string secret, IReadOnlyList<Guid> keys)
    {
        using HttpClient client = httpClientFactory.CreateClient(nameof(SearchIndexNotifier));
        client.Timeout = TimeSpan.FromMinutes(2);
        foreach (Guid[] batch in keys.Chunk(BatchSize))
        {
            if (!await SendAsync(client, url, secret, batch))
            {
                logger.LogWarning("Could not notify the search index of {Count} seeded items; the periodic reindex will add them",
                    keys.Count);
                return;
            }
        }

        logger.LogInformation("Notified the search index of {Count} seeded items", keys.Count);
    }

    private async Task<bool> SendAsync(HttpClient client, string url, string secret, Guid[] batch)
    {
        var delay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(batch.Select(key => new { id = key })),
                };
                request.Headers.Add(ContentWebhookRegistrar.SecretHeader, secret);
                using HttpResponseMessage response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                logger.LogDebug("Search index notification returned {Status} (attempt {Attempt})", (int)response.StatusCode, attempt);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogDebug(ex, "Search index notification failed (attempt {Attempt})", attempt);
            }

            await Task.Delay(delay);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
        }

        return false;
    }
}
