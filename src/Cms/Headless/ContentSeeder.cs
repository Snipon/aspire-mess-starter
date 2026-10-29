using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Sync;
using Umbraco.Extensions;

namespace Cms.Headless;

/// <summary>
/// Idempotent demo content on top of the uSync schema (src/Cms/uSync, imported on first boot before this runs):
/// generated placeholder images, a "Home" site node with blocks, an "Articles" page and <see cref="DemoArticles.Count"/>
/// articles. Runs only if the "site" document type exists, in one scope (transaction and media files), so a failed seed
/// leaves nothing behind and is retried on the next boot. When the site was seeded before, only the demo articles missing
/// from the Articles page (by name) are added, so an install seeded with fewer articles is topped up, and an Articles
/// page still showing the previously seeded "latest articles" block gets the article list instead. Everything created
/// is then sent to the search index (<see cref="SearchIndexNotifier"/>).
/// </summary>
internal sealed class ContentSeeder(
    IRuntimeState runtimeState,
    ICoreScopeProvider scopeProvider,
    IServerRoleAccessor serverRoleAccessor,
    IContentTypeService contentTypeService,
    IContentService contentService,
    IMediaService mediaService,
    MediaFileManager mediaFileManager,
    MediaUrlGeneratorCollection mediaUrlGenerators,
    IShortStringHelper shortStringHelper,
    IContentTypeBaseServiceProvider contentTypeBaseServiceProvider,
    SearchIndexNotifier searchIndexNotifier,
    ILogger<ContentSeeder> logger) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string SiteAlias = "site";
    private const int ImageCount = 12;

    // Placeholder image palettes (start/end colours of the gradient).
    private static readonly (Rgb24 From, Rgb24 To)[] Palettes =
    [
        (new(30, 60, 170), new(120, 200, 255)), (new(90, 20, 120), new(250, 120, 160)), (new(10, 90, 80), new(140, 230, 170)),
        (new(160, 60, 20), new(255, 200, 90)), (new(20, 30, 60), new(90, 130, 200)), (new(120, 20, 40), new(250, 150, 90)),
        (new(30, 100, 140), new(200, 240, 220)), (new(60, 40, 140), new(150, 210, 250)), (new(40, 80, 30), new(220, 230, 110)),
        (new(100, 40, 80), new(170, 180, 255)), (new(15, 50, 90), new(250, 170, 120)), (new(50, 50, 55), new(190, 190, 200)),
    ];

    public Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level != RuntimeLevel.Run || serverRoleAccessor.CurrentServerRole == ServerRole.Subscriber)
        {
            return Task.CompletedTask;
        }

        IContentType? site = contentTypeService.Get(SiteAlias);
        if (site is null)
        {
            logger.LogWarning("Document type {Alias} is missing (uSync import did not run?), skipping demo content", SiteAlias);
            return Task.CompletedTask;
        }

        if (contentService.GetRootContent().FirstOrDefault(c => c.ContentType.Alias == SiteAlias) is { } existingHome)
        {
            UpdateExistingSite(existingHome);
            return Task.CompletedTask;
        }

        logger.LogInformation("Seeding demo content");
        using ICoreScope scope = scopeProvider.CreateCoreScope(scopeFileSystems: true);
        IMedia folder = mediaService.CreateMediaWithIdentity("Placeholders", Constants.System.Root, Constants.Conventions.MediaTypes.Folder);
        IMedia[] images = [.. Enumerable.Range(1, ImageCount).Select(i => CreateImage(folder.Id, i))];

        Guid hero = ElementTypeKey("heroBlock"), cta = ElementTypeKey("ctaBlock"), latest = ElementTypeKey("latestArticlesBlock");
        var created = new List<Guid>();
        var articlesKey = Guid.NewGuid();

        IContent home = CreateAndPublish("Home", Constants.System.Root, SiteAlias, Guid.NewGuid(), new()
        {
            ["blocks"] = Blocks(
                (hero, new()
                {
                    ["heading"] = "Notes on building for the web",
                    ["subheading"] = "Articles about .NET, cloud, front-end engineering and design systems, served by a headless Umbraco CMS.",
                    ["image"] = MediaPicker(images[0]),
                    ["ctaLabel"] = "Browse all articles",
                    ["ctaLink"] = DocumentLink("Browse all articles", articlesKey),
                }),
                (latest, new() { ["heading"] = "Latest articles", ["count"] = 6 }),
                (cta, new()
                {
                    ["heading"] = "Built with Aspire",
                    ["text"] = "This demo runs Umbraco, a typed .NET API, Meilisearch and an Astro front end from a single AppHost.",
                    ["link"] = ExternalLink("Explore Aspire", "https://aspire.dev"),
                    ["style"] = JsonSerializer.Serialize(new[] { "primary" }),
                })),
        });

        IContent articles = CreateAndPublish("Articles", home.Id, "page", articlesKey, new()
        {
            ["title"] = "Articles",
            ["blocks"] = ArticlesPageBlocks(images),
        });
        created.AddRange([home.Key, articles.Key]);

        IReadOnlyList<DemoArticle> demoArticles = DemoArticles.Generate(DateTime.UtcNow);
        created.AddRange(CreateArticles(articles.Id, demoArticles, images));

        scope.Complete();
        logger.LogInformation("Seeded {Count} articles", demoArticles.Count);
        searchIndexNotifier.NotifyInBackground(created);
        return Task.CompletedTask;
    }

    private string ArticlesPageBlocks(IMedia[] images) => Blocks(
        (ElementTypeKey("heroBlock"), new()
        {
            ["heading"] = "Articles",
            ["subheading"] = "Everything we have written, newest first.",
            ["image"] = MediaPicker(images[1 % images.Length]),
        }),
        (ElementTypeKey("articleListBlock"), new() { ["heading"] = "All articles", ["pageSize"] = 12 }));

    private void UpdateExistingSite(IContent home)
    {
        const int maxChildren = 100_000;
        IContent? articles = contentService.GetPagedChildren(home.Id, 0, maxChildren, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "page" && c.Name == "Articles");
        IMedia? folder = mediaService.GetRootMedia().FirstOrDefault(m => m.Name == "Placeholders");
        if (articles is null || folder is null)
        {
            return; // Restructured by hand: leave it alone.
        }

        var existing = contentService.GetPagedChildren(articles.Id, 0, maxChildren, out _)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        DemoArticle[] missing = [.. DemoArticles.Generate(DateTime.UtcNow).Where(a => !existing.Contains(a.Title))];
        IMedia[] images = [.. mediaService.GetPagedChildren(folder.Id, 0, maxChildren, out _)];
        bool upgradeArticlesPage = HasOnlyBlocks(articles, "heroBlock", "latestArticlesBlock")
            && contentTypeService.Get("articleListBlock") is not null;
        if (images.Length == 0 || (missing.Length == 0 && !upgradeArticlesPage))
        {
            return;
        }

        var changed = new List<Guid>();
        using ICoreScope scope = scopeProvider.CreateCoreScope(scopeFileSystems: true);
        if (upgradeArticlesPage)
        {
            logger.LogInformation("Replacing the Articles page's latest articles block with the article list");
            articles.SetValue("blocks", ArticlesPageBlocks(images));
            Publish(articles);
            changed.Add(articles.Key);
        }

        if (missing.Length > 0)
        {
            logger.LogInformation("Adding {Count} missing demo articles", missing.Length);
            changed.AddRange(CreateArticles(articles.Id, missing, images));
        }

        scope.Complete();
        logger.LogInformation("Updated the demo content ({Count} articles added)", missing.Length);
        searchIndexNotifier.NotifyInBackground(changed);
    }

    /// <summary>Whether the page's blocks are exactly one of each of the given element types, in order (the seeded layout).</summary>
    private bool HasOnlyBlocks(IContent page, params string[] elementTypeAliases)
    {
        if (page.GetValue<string>("blocks") is not { Length: > 0 } json)
        {
            return false;
        }

        using var document = JsonDocument.Parse(json);
        string?[] keys = [.. document.RootElement.GetProperty("contentData").EnumerateArray()
            .Select(block => block.GetProperty("contentTypeKey").GetString())];
        string?[] expected = [.. elementTypeAliases.Select(alias => contentTypeService.Get(alias)?.Key.ToString())];
        return keys.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
    }

    private List<Guid> CreateArticles(int parentId, IReadOnlyList<DemoArticle> articles, IMedia[] images)
    {
        var keys = new List<Guid>(articles.Count);
        for (var i = 0; i < articles.Count; i++)
        {
            DemoArticle article = articles[i];
            keys.Add(Guid.NewGuid());
            CreateAndPublish(article.Title, parentId, "article", keys[^1], new()
            {
                ["title"] = article.Title,
                ["image"] = MediaPicker(images[i % images.Length]),
                ["body"] = article.Body,
                ["tags"] = JsonSerializer.Serialize(article.Tags),
                ["publishDate"] = article.PublishDate,
            });
        }

        return keys;
    }

    private Guid ElementTypeKey(string alias) =>
        contentTypeService.Get(alias)?.Key ?? throw new InvalidOperationException($"Element type '{alias}' is missing");

    private IContent CreateAndPublish(string name, int parentId, string contentTypeAlias, Guid key, Dictionary<string, object?> values)
    {
        IContent content = contentService.Create(name, parentId, contentTypeAlias);
        content.Key = key;
        foreach ((string alias, object? value) in values)
        {
            content.SetValue(alias, value);
        }

        Publish(content);
        return content;
    }

    private void Publish(IContent content)
    {
        contentService.Save(content);
        PublishResult result = contentService.Publish(content, ["*"]);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Could not publish '{content.Name}': {result.Result}");
        }
    }

    /// <summary>Block List value (v15+ format: layout, contentData and expose; no settings).</summary>
    private static string Blocks(params (Guid ElementTypeKey, Dictionary<string, object> Values)[] blocks)
    {
        Guid[] keys = [.. blocks.Select(_ => Guid.NewGuid())];
        return JsonSerializer.Serialize(new
        {
            layout = new Dictionary<string, object> { [Constants.PropertyEditors.Aliases.BlockList] = keys.Select(k => new { contentKey = k }) },
            contentData = blocks.Select((b, i) => new
            {
                key = keys[i],
                contentTypeKey = b.ElementTypeKey,
                values = b.Values.Select(v => new { alias = v.Key, value = v.Value, culture = (string?)null, segment = (string?)null }),
            }),
            settingsData = Array.Empty<object>(),
            expose = keys.Select(k => new { contentKey = k, culture = (string?)null, segment = (string?)null }),
        });
    }

    // Stored (database) value formats, which are JSON strings both for properties and for values inside blocks.
    private static string MediaPicker(IMedia media) => JsonSerializer.Serialize(new[] { new { key = Guid.NewGuid(), mediaKey = media.Key } });

    private static string DocumentLink(string name, Guid documentKey) =>
        JsonSerializer.Serialize(new[] { new { name, udi = Udi.Create(Constants.UdiEntityType.Document, documentKey).ToString() } });

    private static string ExternalLink(string name, string url) => JsonSerializer.Serialize(new[] { new { name, url } });

    private IMedia CreateImage(int parentId, int index)
    {
        // Generated diagonal gradient with soft rings, so the seed needs no binary assets in the repo.
        const int width = 1600, height = 900;
        (Rgb24 from, Rgb24 to) = Palettes[(index - 1) % Palettes.Length];
        float cx = width * (0.2f + 0.6f * ((index * 37) % 10) / 10f), cy = height * (0.25f + 0.5f * ((index * 53) % 10) / 10f);
        using var image = new Image<Rgb24>(width, height);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < height; y++)
            {
                Span<Rgb24> row = rows.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    float t = (x / (float)width + y / (float)height) / 2f;
                    float ring = 0.5f + 0.5f * MathF.Cos(MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / 28f);
                    float mix = Math.Clamp(t + 0.12f * ring - 0.06f, 0f, 1f);
                    row[x] = new Rgb24(Lerp(from.R, to.R, mix), Lerp(from.G, to.G, mix), Lerp(from.B, to.B, mix));
                }
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        stream.Position = 0;

        IMedia media = mediaService.CreateMedia($"Placeholder {index:00}", parentId, Constants.Conventions.MediaTypes.Image);
        media.SetValue(mediaFileManager, mediaUrlGenerators, shortStringHelper, contentTypeBaseServiceProvider,
            Constants.Conventions.Media.File, $"placeholder-{index:00}.jpg", stream);
        mediaService.Save(media);
        return media;
    }

    private static byte Lerp(byte a, byte b, float t) => (byte)(a + (b - a) * t);
}
