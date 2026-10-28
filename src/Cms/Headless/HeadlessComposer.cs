using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Models;

namespace Cms.Headless;

/// <summary>
/// Headless-only CMS: templates can never be created, and document types are always saved without templates.
/// Seeds demo content and registers the search-index webhook once Umbraco has started.
/// </summary>
public sealed class HeadlessComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<ContentWebhookOptions>(builder.Config.GetSection(ContentWebhookOptions.SectionName));

        // Only the default webhook events are registered out of the box. ContentMovedToRecycleBin (used by the
        // search-index webhook, since trashing does not raise Unpublish) is an extended event, so register all content
        // events, with the same payload type Umbraco is configured to use.
        var payloadType = builder.Config.GetSection("Umbraco:CMS:Webhook").Get<WebhookSettings>()?.PayloadType
            ?? new WebhookSettings().PayloadType;
        builder.WebhookEvents().AddCms(cms => cms.AddContent(onlyDefault: false, payloadType));

        builder
            .AddNotificationHandler<TemplateSavingNotification, BlockTemplatesHandler>()
            .AddNotificationHandler<ContentTypeSavingNotification, StripTemplatesHandler>()
            .AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, ContentSeeder>()
            .AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, ContentWebhookRegistrar>();
    }
}

internal sealed class BlockTemplatesHandler : INotificationHandler<TemplateSavingNotification>
{
    public void Handle(TemplateSavingNotification notification) =>
        notification.CancelOperation(new EventMessage(
            "Headless",
            "Templates are disabled: this Umbraco instance is headless and only serves content through the Delivery API.",
            EventMessageType.Error));
}

internal sealed class StripTemplatesHandler : INotificationHandler<ContentTypeSavingNotification>
{
    public void Handle(ContentTypeSavingNotification notification)
    {
        foreach (IContentType contentType in notification.SavedEntities)
        {
            if (contentType.DefaultTemplate is not null)
            {
                contentType.SetDefaultTemplate(null);
            }

            if (contentType.AllowedTemplates?.Any() == true)
            {
                contentType.AllowedTemplates = [];
            }
        }
    }
}
