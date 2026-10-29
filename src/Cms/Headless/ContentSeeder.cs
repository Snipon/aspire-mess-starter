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
/// generated placeholder images, a "Home" site node with blocks, an "Articles" page with <see cref="DemoArticles.Count"/>
/// articles and a "Blog" page with <see cref="DemoBlogPosts.Count"/> blog posts. Runs only if the "site" document type
/// exists, in one scope (transaction and media files), so a failed seed leaves nothing behind and is retried on the next
/// boot. When the site was seeded before, only the demo articles and blog posts missing from their pages (by name) are
/// added, so an install seeded with fewer is topped up; an Articles page still showing the previously seeded "latest
/// articles" block gets the article list instead, and a Home page still in the layout seeded before blog posts existed
/// gets the blog post slider and a Blog page. Everything created is then sent to the search index
/// (<see cref="SearchIndexNotifier"/>).
/// <para>
/// Opt-in: runs only when <c>DemoContent:Enabled</c> is <c>true</c> (set by apphost.cs and infra/terraform for this
/// demo). Without it the CMS starts with the schema and no content; a project built on this repo leaves it off, or
/// deletes this class together with <see cref="DemoArticles"/> and <see cref="DemoBlogPosts"/>.
/// </para>
/// </summary>
internal sealed class ContentSeeder(
    IConfiguration configuration,
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
    internal const string EnabledSetting = "DemoContent:Enabled";
    private const string SiteAlias = "site";
    private const int ImageCount = 12;
    private const int MaxChildren = 100_000;

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

        if (!configuration.GetValue<bool>(EnabledSetting))
        {
            logger.LogInformation("Demo content is off ({Setting} is not true), not seeding", EnabledSetting);
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

        var created = new List<Guid>();
        var articlesKey = Guid.NewGuid();

        IContent home = CreateAndPublish("Home", Constants.System.Root, SiteAlias, Guid.NewGuid(), new()
        {
            ["blocks"] = HomeBlocks(images, articlesKey),
        });

        IContent articles = CreateAndPublish("Articles", home.Id, "page", articlesKey, new()
        {
            ["title"] = "Articles",
            ["blocks"] = ArticlesPageBlocks(images),
        });
        created.AddRange([home.Key, articles.Key]);

        IReadOnlyList<DemoArticle> demoArticles = DemoArticles.Generate(DateTime.UtcNow);
        List<Guid> articleKeys = CreateArticles(articles.Id, demoArticles, images);
        created.AddRange(articleKeys);

        IReadOnlyList<DemoBlogPost> demoPosts = DemoBlogPosts.Generate(DateTime.UtcNow, demoArticles);
        IContent blog = CreateBlogPage(home.Id, images);
        created.Add(blog.Key);
        created.AddRange(CreateBlogPosts(blog.Id, demoPosts, images,
            demoArticles.Select((a, i) => (a.Title, articleKeys[i])).ToDictionary(StringComparer.OrdinalIgnoreCase)));

        scope.Complete();
        logger.LogInformation("Seeded {Articles} articles and {Posts} blog posts", demoArticles.Count, demoPosts.Count);
        searchIndexNotifier.NotifyInBackground(created);
        return Task.CompletedTask;
    }

    private string HomeBlocks(IMedia[] images, Guid articlesKey) => Blocks(
        (ElementTypeKey("heroBlock"), new()
        {
            ["heading"] = "Notes on building for the web",
            ["subheading"] = "Articles about .NET, cloud, front-end engineering and design systems, served by a headless Umbraco CMS.",
            ["image"] = MediaPicker(images[0]),
            ["ctaLabel"] = "Browse all articles",
            ["ctaLink"] = DocumentLink("Browse all articles", articlesKey),
        }),
        (ElementTypeKey("latestArticlesBlock"), new() { ["heading"] = "Latest articles", ["count"] = 6 }),
        (ElementTypeKey("latestBlogPostsBlock"), new() { ["heading"] = "From the blog", ["count"] = 8 }),
        (ElementTypeKey("ctaBlock"), new()
        {
            ["heading"] = "Built with Aspire",
            ["text"] = "This demo runs Umbraco, a typed .NET API, Meilisearch and an Astro front end from a single AppHost.",
            ["link"] = ExternalLink("Explore Aspire", "https://aspire.dev"),
            ["style"] = JsonSerializer.Serialize(new[] { "primary" }),
        }));

    private IContent CreateBlogPage(int homeId, IMedia[] images) => CreateAndPublish("Blog", homeId, "page", Guid.NewGuid(), new()
    {
        ["title"] = "Blog",
        ["blocks"] = Blocks(
            (ElementTypeKey("heroBlock"), new()
            {
                ["heading"] = "Blog",
                ["subheading"] = "Shorter notes from the team, each pointing to an article for the long version.",
                ["image"] = MediaPicker(images[2 % images.Length]),
            }),
            (ElementTypeKey("blogPostListBlock"), new() { ["heading"] = "All posts", ["pageSize"] = 12 })),
    });

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
        IContent[] pages = [.. contentService.GetPagedChildren(home.Id, 0, MaxChildren, out _).Where(c => c.ContentType.Alias == "page")];
        IContent? articles = pages.FirstOrDefault(c => c.Name == "Articles");
        IMedia? folder = mediaService.GetRootMedia().FirstOrDefault(m => m.Name == "Placeholders");
        if (articles is null || folder is null)
        {
            return; // Restructured by hand: leave it alone.
        }

        Dictionary<string, Guid> articleKeys = ChildKeysByName(articles);
        IReadOnlyList<DemoArticle> demoArticles = DemoArticles.Generate(DateTime.UtcNow);
        DemoArticle[] missing = [.. demoArticles.Where(a => !articleKeys.ContainsKey(a.Title))];
        IMedia[] images = [.. mediaService.GetPagedChildren(folder.Id, 0, MaxChildren, out _)];
        bool upgradeArticlesPage = HasOnlyBlocks(articles, "heroBlock", "latestArticlesBlock")
            && contentTypeService.Get("articleListBlock") is not null;

        // The Blog page is only created together with the home page's slider (Home still in the layout seeded before
        // blog posts existed), so deleting it by hand later is respected.
        bool hasBlogSchema = new[] { "blogPost", "blogPostListBlock", "latestBlogPostsBlock" }.All(a => contentTypeService.Get(a) is not null);
        bool upgradeHome = hasBlogSchema && HasOnlyBlocks(home, "heroBlock", "latestArticlesBlock", "ctaBlock");
        IContent? blog = pages.FirstOrDefault(c => c.Name == "Blog");
        Dictionary<string, Guid> postKeys = blog is null ? [] : ChildKeysByName(blog);
        DemoBlogPost[] missingPosts = hasBlogSchema && (blog is not null || upgradeHome)
            ? [.. DemoBlogPosts.Generate(DateTime.UtcNow, demoArticles).Where(p => !postKeys.ContainsKey(p.Title))]
            : [];
        if (images.Length == 0 || (missing.Length == 0 && missingPosts.Length == 0 && !upgradeArticlesPage && !upgradeHome))
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
            List<Guid> added = CreateArticles(articles.Id, missing, images);
            changed.AddRange(added);
            for (var i = 0; i < missing.Length; i++)
            {
                articleKeys.TryAdd(missing[i].Title, added[i]);
            }
        }

        if (upgradeHome)
        {
            logger.LogInformation("Adding the blog post slider to the home page");
            home.SetValue("blocks", HomeBlocks(images, articles.Key));
            Publish(home);
            changed.Add(home.Key);
            if (blog is null)
            {
                blog = CreateBlogPage(home.Id, images);
                changed.Add(blog.Key);
            }
        }

        if (blog is not null && missingPosts.Length > 0)
        {
            logger.LogInformation("Adding {Count} missing demo blog posts", missingPosts.Length);
            changed.AddRange(CreateBlogPosts(blog.Id, missingPosts, images, articleKeys));
        }

        scope.Complete();
        logger.LogInformation("Updated the demo content ({Articles} articles and {Posts} blog posts added)", missing.Length, missingPosts.Length);
        searchIndexNotifier.NotifyInBackground(changed);
    }

    private Dictionary<string, Guid> ChildKeysByName(IContent parent)
    {
        var keys = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (IContent child in contentService.GetPagedChildren(parent.Id, 0, MaxChildren, out _))
        {
            keys.TryAdd(child.Name ?? string.Empty, child.Key);
        }

        return keys;
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

    private List<Guid> CreateBlogPosts(int parentId, IReadOnlyList<DemoBlogPost> posts, IMedia[] images, IReadOnlyDictionary<string, Guid> articleKeys)
    {
        var keys = new List<Guid>(posts.Count);
        for (var i = 0; i < posts.Count; i++)
        {
            DemoBlogPost post = posts[i];
            keys.Add(Guid.NewGuid());
            CreateAndPublish(post.Title, parentId, "blogPost", keys[^1], new()
            {
                ["title"] = post.Title,
                ["image"] = MediaPicker(images[(i + 5) % images.Length]),
                ["body"] = post.Body,
                ["relatedArticle"] = articleKeys.TryGetValue(post.Article.Title, out Guid articleKey)
                    ? Udi.Create(Constants.UdiEntityType.Document, articleKey).ToString()
                    : null,
                ["publishDate"] = post.PublishDate,
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
