using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentTypeEditing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Core.Services.ContentTypeEditing;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Sync;
using Umbraco.Extensions;

namespace Cms.Headless;

/// <summary>
/// Idempotent demo seed: document types "home" and "page" (title, summary, body, image),
/// a generated hero image, and a published Home node with a few child pages.
/// Document types are created only if missing; content only if no root "home" node exists.
/// </summary>
internal sealed class ContentSeeder(
    IRuntimeState runtimeState,
    IServerRoleAccessor serverRoleAccessor,
    IContentTypeService contentTypeService,
    IContentTypeEditingService contentTypeEditingService,
    IContentService contentService,
    IMediaService mediaService,
    MediaFileManager mediaFileManager,
    MediaUrlGeneratorCollection mediaUrlGenerators,
    IShortStringHelper shortStringHelper,
    IContentTypeBaseServiceProvider contentTypeBaseServiceProvider,
    ILogger<ContentSeeder> logger) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string HomeAlias = "home";
    private const string PageAlias = "page";
    private static readonly Guid UserKey = Constants.Security.SuperUserKey;

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level != RuntimeLevel.Run || serverRoleAccessor.CurrentServerRole == ServerRole.Subscriber)
        {
            return;
        }

        IContentType page = contentTypeService.Get(PageAlias) ?? await CreateDocumentTypeAsync(PageAlias, "Page", "icon-document", allowedAsRoot: false, []);
        if (!page.AllowedContentTypes?.Any(t => t.Key == page.Key) ?? true)
        {
            // Pages may nest (self-reference is only possible once the type exists).
            page.AllowedContentTypes = [new ContentTypeSort(page.Key, 0, PageAlias)];
            await contentTypeService.UpdateAsync(page, UserKey);
        }

        IContentType home = contentTypeService.Get(HomeAlias)
            ?? await CreateDocumentTypeAsync(HomeAlias, "Home", "icon-home", allowedAsRoot: true, [new ContentTypeSort(page.Key, 0, PageAlias)]);

        if (contentService.GetRootContent().Any(c => c.ContentType.Alias == home.Alias))
        {
            return;
        }

        logger.LogInformation("Seeding demo content");
        IMedia hero = CreateHeroImage();

        IContent homeNode = CreateAndPublish(
            "Home", Constants.System.Root, HomeAlias,
            title: "Welcome to the Aspire demo",
            summary: "A headless Umbraco CMS, a typed .NET API with Meilisearch, and an Astro front end, orchestrated by Aspire.",
            body: "<p>This site is rendered by <strong>Astro</strong>. Content is authored in <strong>Umbraco</strong> and delivered through a typed API.</p><p>Try the search page to query content indexed in <strong>Meilisearch</strong>.</p>",
            image: hero);

        IContent about = CreateAndPublish(
            "About", homeNode.Id, PageAlias,
            title: "About this project",
            summary: "How the pieces fit together.",
            body: "<h2>Architecture</h2><p>The web app only talks to the API. The API talks to the CMS and the search engine through swappable adapters.</p><ul><li>Umbraco Delivery API</li><li>Meilisearch</li><li>Astro SSR</li></ul>",
            image: hero);

        CreateAndPublish(
            "Team", about.Id, PageAlias,
            title: "The team",
            summary: "The people behind the demo.",
            body: "<p>A small team that likes simple, typed boundaries between systems.</p>",
            image: null);

        CreateAndPublish(
            "Services", homeNode.Id, PageAlias,
            title: "Our services",
            summary: "Content modelling, search and front-end delivery.",
            body: "<p>We help teams build fast, content-driven websites with headless CMSes and modern search.</p><p>Every page is indexed for full-text search on publish.</p>",
            image: hero);

        CreateAndPublish(
            "Contact", homeNode.Id, PageAlias,
            title: "Contact us",
            summary: "Get in touch.",
            body: "<p>Send an email to <a href=\"mailto:hello@example.com\">hello@example.com</a>.</p>",
            image: null);
    }

    private async Task<IContentType> CreateDocumentTypeAsync(string alias, string name, string icon, bool allowedAsRoot, ContentTypeSort[] allowedChildren)
    {
        var groupKey = Guid.NewGuid();
        ContentTypePropertyTypeModel Property(string propertyAlias, string propertyName, Guid dataTypeKey, int sortOrder, bool mandatory = false) => new()
        {
            Key = Guid.NewGuid(),
            ContainerKey = groupKey,
            Alias = propertyAlias,
            Name = propertyName,
            DataTypeKey = dataTypeKey,
            SortOrder = sortOrder,
            Validation = new PropertyTypeValidation { Mandatory = mandatory },
        };

        var model = new ContentTypeCreateModel
        {
            Alias = alias,
            Name = name,
            Icon = icon,
            AllowedAsRoot = allowedAsRoot,
            AllowedContentTypes = allowedChildren,
            Containers = [new ContentTypePropertyContainerModel { Key = groupKey, Name = "Content", Type = "Group", SortOrder = 0 }],
            Properties =
            [
                Property("title", "Title", Constants.DataTypes.Guids.TextstringGuid, 0, mandatory: true),
                Property("summary", "Summary", Constants.DataTypes.Guids.TextareaGuid, 1),
                Property("body", "Body", Constants.DataTypes.Guids.RichtextEditorGuid, 2),
                Property("image", "Image", Constants.DataTypes.Guids.MediaPicker3SingleImageGuid, 3),
            ],
        };

        Attempt<IContentType?, ContentTypeOperationStatus> result = await contentTypeEditingService.CreateAsync(model, UserKey);
        if (!result.Success || result.Result is null)
        {
            throw new InvalidOperationException($"Could not create document type '{alias}': {result.Status}");
        }

        logger.LogInformation("Created document type {Alias}", alias);
        return result.Result;
    }

    private IContent CreateAndPublish(string name, int parentId, string contentTypeAlias, string title, string summary, string body, IMedia? image)
    {
        IContent content = contentService.Create(name, parentId, contentTypeAlias);
        content.SetValue("title", title);
        content.SetValue("summary", summary);
        content.SetValue("body", body);
        if (image is not null)
        {
            // Media Picker 3 value format.
            content.SetValue("image", JsonSerializer.Serialize(new[] { new { key = Guid.NewGuid(), mediaKey = image.Key } }));
        }

        contentService.Save(content);
        PublishResult result = contentService.Publish(content, ["*"]);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Could not publish '{name}': {result.Result}");
        }

        return content;
    }

    private IMedia CreateHeroImage()
    {
        // Generated gradient so the seed needs no binary assets in the repo.
        const int width = 1920, height = 1080;
        using var image = new Image<Rgb24>(width, height);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < height; y++)
            {
                Span<Rgb24> row = rows.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    row[x] = new Rgb24((byte)(30 + (x * 120 / width)), (byte)(60 + (y * 100 / height)), 170);
                }
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        stream.Position = 0;

        IMedia media = mediaService.CreateMedia("Hero", Constants.System.Root, Constants.Conventions.MediaTypes.Image);
        media.SetValue(mediaFileManager, mediaUrlGenerators, shortStringHelper, contentTypeBaseServiceProvider, Constants.Conventions.Media.File, "hero.jpg", stream);
        mediaService.Save(media);
        return media;
    }
}
