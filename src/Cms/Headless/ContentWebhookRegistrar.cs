using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;

namespace Cms.Headless;

public sealed class ContentWebhookOptions
{
    public const string SectionName = "ContentWebhook";

    /// <summary>Absolute URL the content events are POSTed to (the API's /api/webhooks/content).</summary>
    public string? TargetUrl { get; set; }

    /// <summary>Shared secret sent in the <c>X-Webhook-Secret</c> header.</summary>
    public string? Secret { get; set; }
}

/// <summary>
/// Idempotently creates/updates the Umbraco webhook that notifies the API when content changes,
/// so the search index stays in sync. Identified by its name; URL, secret and events are reconciled on every start.
/// </summary>
internal sealed class ContentWebhookRegistrar(
    IRuntimeState runtimeState,
    IWebhookService webhookService,
    IOptions<ContentWebhookOptions> options,
    ILogger<ContentWebhookRegistrar> logger) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string WebhookName = "Search index sync";
    private const string SecretHeader = "X-Webhook-Secret";

    // Payloads (camelCase JSON, event alias in the Umb-Webhook-Event header):
    //   Umbraco.ContentPublish          -> { "id": "<guid>", "cultures": [{ "culture": "en-US", "date": "..." }] }
    //   Umbraco.ContentUnpublish        -> { "id": "<guid>" }
    //   Umbraco.ContentDelete           -> { "id": "<guid>" }
    //   Umbraco.ContentMovedToRecycleBin -> [ { "id": "<guid>" } ]  (trashing does not raise Unpublish)
    //   Umbraco.ContentMoved            -> [ { "id": "<guid>" } ]  (moving a published node changes its path and its
    //                                      descendants' paths without a publish; the API re-syncs the subtree)
    // The webhook secret is stored as a plain custom header, so backoffice users with access to Settings > Webhooks can
    // read it; it is reconciled from configuration on every start, so rotate it via config (see docs/deployment.md).
    private static readonly string[] Events =
    [
        Constants.WebhookEvents.Aliases.ContentPublish,
        Constants.WebhookEvents.Aliases.ContentUnpublish,
        Constants.WebhookEvents.Aliases.ContentDelete,
        Constants.WebhookEvents.Aliases.ContentMovedToRecycleBin,
        Constants.WebhookEvents.Aliases.ContentMoved,
    ];

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level != RuntimeLevel.Run)
        {
            return;
        }

        ContentWebhookOptions settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.TargetUrl) || string.IsNullOrWhiteSpace(settings.Secret))
        {
            logger.LogWarning("ContentWebhook:TargetUrl/Secret not configured; search index webhook not registered");
            return;
        }

        var headers = new Dictionary<string, string> { [SecretHeader] = settings.Secret };

        IWebhook? existing = (await webhookService.GetAllAsync(0, int.MaxValue)).Items
            .FirstOrDefault(w => w.Name == WebhookName);

        if (existing is null)
        {
            var webhook = new Webhook(settings.TargetUrl, enabled: true, events: Events, headers: headers)
            {
                Name = WebhookName,
                Description = "Keeps the API's search index in sync with published content.",
            };
            Attempt<IWebhook, WebhookOperationStatus> created = await webhookService.CreateAsync(webhook);
            LogResult(created.Success, created.Status, "created");
            return;
        }

        bool upToDate = existing.Enabled
            && existing.Url == settings.TargetUrl
            && existing.Events.Order().SequenceEqual(Events.Order())
            && existing.Headers.TryGetValue(SecretHeader, out var currentSecret) && currentSecret == settings.Secret;
        if (upToDate)
        {
            return;
        }

        existing.Url = settings.TargetUrl;
        existing.Enabled = true;
        existing.Events = Events;
        existing.Headers = headers;
        Attempt<IWebhook, WebhookOperationStatus> updated = await webhookService.UpdateAsync(existing);
        LogResult(updated.Success, updated.Status, "updated");
    }

    private void LogResult(bool success, WebhookOperationStatus status, string action)
    {
        if (success)
        {
            logger.LogInformation("Search index webhook {Action}", action);
        }
        else
        {
            logger.LogError("Search index webhook could not be {Action}: {Status}", action, status);
        }
    }
}
