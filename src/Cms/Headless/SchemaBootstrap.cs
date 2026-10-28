// TEMPORARY: creates the schema once so uSync (ExportOnSave) writes src/Cms/uSync/v18. Deleted afterwards.
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentTypeEditing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.ContentTypeEditing;

namespace Cms.Headless;

internal sealed class SchemaBootstrap(
    IDataTypeService dataTypeService,
    PropertyEditorCollection editors,
    IConfigurationEditorJsonSerializer serializer,
    IContentTypeService contentTypeService,
    IContentTypeEditingService editing,
    ILogger<SchemaBootstrap> logger) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private static readonly Guid U = Constants.Security.SuperUserKey;

    // Data types
    static readonly Guid SingleLink = Guid.Parse("6d3c3b0e-1c1a-4f7e-9f51-6a1b8f2b7a01");
    static readonly Guid CtaStyle = Guid.Parse("6d3c3b0e-1c1a-4f7e-9f51-6a1b8f2b7a02");
    static readonly Guid ArticleCount = Guid.Parse("6d3c3b0e-1c1a-4f7e-9f51-6a1b8f2b7a03");
    static readonly Guid Markdown = Guid.Parse("6d3c3b0e-1c1a-4f7e-9f51-6a1b8f2b7a04");
    static readonly Guid ArticleTags = Guid.Parse("6d3c3b0e-1c1a-4f7e-9f51-6a1b8f2b7a05");
    static readonly Guid Blocks = Guid.Parse("6d3c3b0e-1c1a-4f7e-9f51-6a1b8f2b7a06");
    // Types
    static readonly Guid Hero = Guid.Parse("7a1e5c2d-3b4f-4a6e-8d9c-0e1f2a3b4c01");
    static readonly Guid Cta = Guid.Parse("7a1e5c2d-3b4f-4a6e-8d9c-0e1f2a3b4c02");
    static readonly Guid Latest = Guid.Parse("7a1e5c2d-3b4f-4a6e-8d9c-0e1f2a3b4c03");
    static readonly Guid Site = Guid.Parse("7a1e5c2d-3b4f-4a6e-8d9c-0e1f2a3b4c04");
    static readonly Guid Page = Guid.Parse("7a1e5c2d-3b4f-4a6e-8d9c-0e1f2a3b4c05");
    static readonly Guid Article = Guid.Parse("7a1e5c2d-3b4f-4a6e-8d9c-0e1f2a3b4c06");

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (contentTypeService.Get("site") is not null)
        {
            return;
        }

        logger.LogWarning("Bootstrapping schema");
        await DataType(SingleLink, "Single Link", Constants.PropertyEditors.Aliases.MultiUrlPicker, "Umb.PropertyEditorUi.MultiUrlPicker",
            new() { ["minNumber"] = 0, ["maxNumber"] = 1, ["ignoreUserStartNodes"] = false, ["hideAnchor"] = false });
        await DataType(CtaStyle, "CTA Style", Constants.PropertyEditors.Aliases.DropDownListFlexible, "Umb.PropertyEditorUi.Dropdown",
            new() { ["multiple"] = false, ["items"] = new[] { "primary", "secondary" } });
        await DataType(ArticleCount, "Article Count", Constants.PropertyEditors.Aliases.Integer, "Umb.PropertyEditorUi.Integer",
            new() { ["min"] = 1, ["max"] = 50, ["step"] = 1 });
        await DataType(Markdown, "Markdown", Constants.PropertyEditors.Aliases.MarkdownEditor, "Umb.PropertyEditorUi.MarkdownEditor",
            new() { ["preview"] = true, ["overlaySize"] = "medium" });
        await DataType(ArticleTags, "Article Tags", Constants.PropertyEditors.Aliases.Tags, "Umb.PropertyEditorUi.Tags",
            new() { ["group"] = "articles", ["storageType"] = "Json" });

        Guid text = Constants.DataTypes.Guids.TextstringGuid, area = Constants.DataTypes.Guids.TextareaGuid,
            image = Constants.DataTypes.Guids.MediaPicker3SingleImageGuid, date = Constants.DataTypes.Guids.DatePickerGuid;

        await Type(Hero, "heroBlock", "Hero", "icon-picture", element: true, [], [
            P("heading", "Heading", text, true), P("subheading", "Subheading", area), P("image", "Image", image),
            P("ctaLabel", "CTA label", text), P("ctaLink", "CTA link", SingleLink)]);
        await Type(Cta, "ctaBlock", "Call to action", "icon-hand-pointer", element: true, [], [
            P("heading", "Heading", text, true), P("text", "Text", area), P("link", "Link", SingleLink), P("style", "Style", CtaStyle)]);
        await Type(Latest, "latestArticlesBlock", "Latest articles", "icon-bulleted-list", element: true, [], [
            P("heading", "Heading", text), P("count", "Count", ArticleCount, description: "Number of articles to show (default 6)."),
            P("tag", "Tag", text, description: "Optional: only show articles with this tag.")]);

        await DataType(Blocks, "Content Blocks", Constants.PropertyEditors.Aliases.BlockList, "Umb.PropertyEditorUi.BlockList",
            new()
            {
                ["blocks"] = new[]
                {
                    Block(Hero, "Hero: {=heading}"),
                    Block(Latest, "Latest articles: {=heading}"),
                    Block(Cta, "CTA: {=heading}"),
                },
                ["validationLimit"] = new Dictionary<string, object>(),
                ["useLiveEditing"] = false,
                ["useInlineEditingAsDefault"] = false,
            });

        await Type(Article, "article", "Article", "icon-newspaper", element: false, [], [
            P("title", "Title", text, true), P("image", "Image", image), P("body", "Body", Markdown),
            P("tags", "Tags", ArticleTags), P("publishDate", "Publish date", date, description: "Falls back to the create date when empty.")]);
        await Type(Page, "page", "Page", "icon-document", element: false, [new ContentTypeSort(Article, 1, "article")], [
            P("title", "Title", text, true), P("blocks", "Blocks", Blocks)]);
        IContentType page = contentTypeService.Get(Page)!;
        page.AllowedContentTypes = [new ContentTypeSort(Page, 0, "page"), new ContentTypeSort(Article, 1, "article")];
        await contentTypeService.UpdateAsync(page, U);
        await Type(Site, "site", "Site", "icon-home", element: false, [new ContentTypeSort(Page, 0, "page")], [
            P("blocks", "Blocks", Blocks)], allowedAsRoot: true);
    }

    static Dictionary<string, object> Block(Guid key, string label) => new()
    {
        ["contentElementTypeKey"] = key,
        ["label"] = label,
        ["editorSize"] = "medium",
        ["forceHideContentEditorInOverlay"] = false,
    };

    static (string Alias, string Name, Guid DataType, bool Mandatory, string? Description) P(
        string alias, string name, Guid dataType, bool mandatory = false, string? description = null) => (alias, name, dataType, mandatory, description);

    async Task DataType(Guid key, string name, string editorAlias, string uiAlias, Dictionary<string, object> config)
    {
        var dataType = new DataType(editors[editorAlias]!, serializer)
        {
            Key = key,
            Name = name,
            EditorUiAlias = uiAlias,
            ConfigurationData = config,
        };
        var result = await dataTypeService.CreateAsync(dataType, U);
        if (!result.Success) throw new InvalidOperationException($"{name}: {result.Status}");
    }

    async Task Type(Guid key, string alias, string name, string icon, bool element, ContentTypeSort[] children,
        (string Alias, string Name, Guid DataType, bool Mandatory, string? Description)[] props, bool allowedAsRoot = false)
    {
        var group = Guid.NewGuid();
        var model = new ContentTypeCreateModel
        {
            Key = key,
            Alias = alias,
            Name = name,
            Icon = icon,
            IsElement = element,
            AllowedAsRoot = allowedAsRoot,
            AllowedContentTypes = children,
            Containers = [new ContentTypePropertyContainerModel { Key = group, Name = "Content", Type = "Group", SortOrder = 0 }],
            Properties = props.Select((p, i) => new ContentTypePropertyTypeModel
            {
                Key = Guid.NewGuid(),
                ContainerKey = group,
                Alias = p.Alias,
                Name = p.Name,
                Description = p.Description,
                DataTypeKey = p.DataType,
                SortOrder = i,
                Validation = new PropertyTypeValidation { Mandatory = p.Mandatory },
            }).ToArray(),
        };
        var result = await editing.CreateAsync(model, U);
        if (!result.Success) throw new InvalidOperationException($"{alias}: {result.Status}");
    }
}
