using System.Text.Json;
using Api.Content.Umbraco;
using Api.Content.Umbraco.Delivery;
using Api.Contracts;
using Api.Endpoints;

namespace Api.Tests;

/// <summary>Mapping of Delivery API JSON captured from the running CMS (Fixtures/delivery) to the contract.</summary>
public sealed class DeliveryApiMapperTests
{
    private static readonly byte[] Secret = new byte[64];

    private static readonly DeliveryApiMapper Mapper =
        new("https://cms.example.com/", new ImageUrlSigner(Secret), [480, 960, 1440, 1920]);

    // The web defaults ReadFromJsonAsync uses in UmbracoContentSource.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static T Fixture<T>(string name) =>
        Parse<T>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    private static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;

    private static Task<Page> MapAsync(ApiContentResponseModel item, DeliveryApiMapper.LatestArticlesResolver? latest = null) =>
        Mapper.ToPageAsync(item, culture: null, latest, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Maps_image_with_focal_point_and_signed_renditions()
    {
        var page = await MapAsync(Fixture<ApiContentResponseModel>("delivery-api-article.json"));

        Assert.Equal("8d0f2b1e-6a3c-4b9a-9e47-0f0f6a1d2c33", page.Id);
        Assert.Equal("/about-us", page.Path);
        Assert.Equal("About Us", page.Title);
        Assert.Equal("Who we are and what we do.", page.Summary);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 30, 0, TimeSpan.Zero), page.UpdatedAt);
        Assert.Equal(new DateOnly(2026, 10, 1), page.Article?.PublishDate);
        Assert.Empty(page.Blocks);

        // Only properties the spec does not know are passed through raw.
        Assert.Equal(["showInFooter"], page.Properties.Keys);
        Assert.True(page.Properties["showInFooter"].GetBoolean());

        var image = Assert.IsType<Image>(page.Image);
        Assert.Equal("https://cms.example.com/media/abc123/Team Photo.jpg", image.Url);
        Assert.Equal("Team photo", image.Alt); // the media name
        Assert.Equal((1500, 1000), (image.Width, image.Height));
        Assert.Equal(new FocalPoint(0.3, 0.6), image.FocalPoint);

        // Renditions wider than the original are skipped; each is absolute and signed.
        Assert.Equal([480, 960, 1440], image.Srcset.Select(r => r.Width));
        Assert.All(image.Srcset, r =>
        {
            Assert.StartsWith($"https://cms.example.com/media/abc123/Team%20Photo.jpg?width={r.Width}&hmac=", r.Url);
            Assert.Equal(new ImageUrlSigner(Secret).Resize("/media/abc123/Team Photo.jpg", r.Width), r.Url["https://cms.example.com".Length..]);
        });
    }

    [Fact]
    public async Task Maps_site_blocks_and_resolves_latest_articles()
    {
        var item = Fixture<ApiContentResponseModel>("delivery/site.json");
        var requested = new List<(string? Tag, int Count)>();
        ArticleSummary[] latest = [new("a1", "/articles/a1", "A1", null, null, new DateOnly(2026, 10, 1), ["css"])];

        var page = await MapAsync(item, (tag, count, _) =>
        {
            requested.Add((tag, count));
            return Task.FromResult(latest);
        });

        Assert.Equal("site", page.ContentType);
        Assert.Equal("/", page.Path);
        Assert.Equal("Home", page.Title); // no title property: falls back to the node name
        Assert.Null(page.Article);
        Assert.Null(page.Image);
        Assert.Empty(page.Properties);
        Assert.Collection(page.Blocks,
            block =>
            {
                var hero = Assert.IsType<HeroBlock>(block);
                Assert.Equal("Notes on building for the web", hero.Heading);
                Assert.StartsWith("Articles about .NET", hero.Subheading);
                Assert.Equal("https://cms.example.com/media/bpbdjpzm/placeholder-01.jpg", hero.Image?.Url);
                Assert.Equal([480, 960, 1440], hero.Image?.Srcset.Select(r => r.Width));
                Assert.Equal(new Link("/articles", "Browse all articles", null), hero.Cta); // content link -> site path
            },
            block =>
            {
                var list = Assert.IsType<LatestArticlesBlock>(block);
                Assert.Equal("Latest articles", list.Heading);
                Assert.Null(list.Tag);
                Assert.Same(latest, list.Articles);
            },
            block =>
            {
                var cta = Assert.IsType<CtaBlock>(block);
                Assert.Equal("Built with Aspire", cta.Heading);
                Assert.StartsWith("This demo runs Umbraco", cta.Text);
                Assert.Equal(new Link("https://aspire.dev", "Explore Aspire", null), cta.Link);
                Assert.Equal(CtaStyle.Primary, cta.Style);
            });
        Assert.Equal([(null, 6)], requested);
    }

    [Fact]
    public async Task Maps_articles_page_with_empty_cta_link()
    {
        var item = Fixture<ApiContentResponseModel>("delivery/articles-page.json");
        var requested = new List<(string? Tag, int Count)>();

        var page = await MapAsync(item, (tag, count, _) =>
        {
            requested.Add((tag, count));
            return Task.FromResult<ArticleSummary[]>([]);
        });

        Assert.Equal("/articles", page.Path);
        Assert.Equal("Articles", page.Title);
        var hero = Assert.IsType<HeroBlock>(page.Blocks[0]);
        Assert.Null(hero.Cta);
        Assert.Equal("Recently published", Assert.IsType<LatestArticlesBlock>(page.Blocks[1]).Heading);
        Assert.Equal([(null, 12)], requested);
    }

    [Fact]
    public async Task Unresolved_latest_articles_are_empty()
    {
        var item = Fixture<ApiContentResponseModel>("delivery/site.json");

        var page = await MapAsync(item, latest: null);

        Assert.Empty(Assert.Single(page.Blocks.OfType<LatestArticlesBlock>()).Articles);
    }

    [Fact]
    public async Task Maps_article_with_rendered_markdown()
    {
        var item = Fixture<ApiContentResponseModel>("delivery/article.json");

        var page = await MapAsync(item);

        Assert.Equal("article", page.ContentType);
        Assert.Equal("/articles/getting-started-with-net-aspire-for-local-development", page.Path);
        Assert.Equal("Getting Started with .NET Aspire for Local Development", page.Title);
        Assert.Equal("https://cms.example.com/media/bpbdjpzm/placeholder-01.jpg", page.Image?.Url);
        Assert.Empty(page.Blocks);
        Assert.Empty(page.Properties);

        var summary = Assert.IsType<string>(page.Summary);
        Assert.StartsWith("If you have ever inherited a project, you know how much Aspire decisions shape", summary);
        Assert.EndsWith("…", summary);
        Assert.InRange(summary.Length, 100, 160);

        var article = Assert.IsType<Article>(page.Article);
        Assert.Equal(new DateOnly(2026, 10, 28), article.PublishDate);
        Assert.Equal(["aspire", "dotnet", "typescript"], article.Tags);
        Assert.Equal(3, article.ReadingTimeMinutes); // ~480 words of prose at 200 wpm
        Assert.Contains("<h2>Common pitfalls</h2>", article.BodyHtml);
        Assert.Contains("<li>Add telemetry before you need it</li>", article.BodyHtml);
        Assert.Contains("<pre><code class=\"language-csharp\">var builder = WebApplication.CreateBuilder(args);", article.BodyHtml);
        Assert.Contains("<a href=\"https://aspire.dev\">Aspire documentation</a>", article.BodyHtml);
        Assert.DoesNotContain("##", article.BodyHtml);
    }

    [Fact]
    public void Maps_query_items_to_article_summaries_in_order()
    {
        var summaries = Fixture<PagedApiContentResponseModel>("delivery/articles-query.json").Items.Select(Mapper.ToArticleSummary).ToArray();

        Assert.Equal(6, summaries.Length);
        Assert.Equal(summaries.OrderByDescending(s => s.PublishDate).Select(s => s.Id), summaries.Select(s => s.Id));
        var first = summaries[0];
        Assert.Equal("d8360124-a5a7-4865-a637-44fb684412ea", first.Id);
        Assert.Equal("/articles/getting-started-with-net-aspire-for-local-development", first.Path);
        Assert.Equal(new DateOnly(2026, 10, 28), first.PublishDate);
        Assert.Equal(["aspire", "dotnet", "typescript"], first.Tags);
        Assert.NotNull(first.Image);
        Assert.All(summaries, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Summary));
            Assert.InRange(s.Summary!.Length, 1, 160);
            Assert.DoesNotContain("#", s.Summary);
        });
    }

    [Fact]
    public async Task Maps_link_kinds_and_drops_unsafe_or_empty_links()
    {
        var item = Parse<ApiContentResponseModel>("""
            { "id": "00000000-0000-0000-0000-000000000001", "contentType": "page", "name": "P", "route": { "path": "/p/" }, "properties": { "blocks": { "items": [
              { "content": { "contentType": "ctaBlock", "properties": { "heading": "Media", "style": "secondary", "link": [
                { "url": "/media/x/file.pdf", "title": "PDF", "target": "_blank", "linkType": "Media", "queryString": null } ] } } },
              { "content": { "contentType": "ctaBlock", "properties": { "heading": "Anchor", "link": [
                { "url": null, "title": "Team", "linkType": "Content", "queryString": "#team", "route": { "path": "/about/" } } ] } } },
              { "content": { "contentType": "ctaBlock", "properties": { "heading": "Script", "link": [
                { "url": "javascript:alert(1)", "title": "x", "linkType": "External" } ] } } },
              { "content": { "contentType": "ctaBlock", "properties": { "heading": "Empty", "link": null } } },
              { "content": { "contentType": "unknownBlock", "properties": {} } }
            ] } } }
            """);

        var page = await MapAsync(item);

        var ctas = page.Blocks.Cast<CtaBlock>().ToArray(); // the unknown block type is skipped
        Assert.Equal(["Media", "Anchor", "Script", "Empty"], ctas.Select(c => c.Heading));
        Assert.Equal(new Link("https://cms.example.com/media/x/file.pdf", "PDF", "_blank"), ctas[0].Link);
        Assert.Equal(CtaStyle.Secondary, ctas[0].Style);
        Assert.Equal(new Link("/about#team", "Team", null), ctas[1].Link);
        Assert.Null(ctas[2].Link);
        Assert.Null(ctas[3].Link);
        Assert.Equal(CtaStyle.Primary, ctas[3].Style);
    }

    [Fact]
    public async Task Latest_articles_count_defaults_to_6_is_clamped_and_tag_must_be_a_single_tag()
    {
        var item = Parse<ApiContentResponseModel>("""
            { "id": "00000000-0000-0000-0000-000000000001", "contentType": "page", "name": "P", "route": { "path": "/p/" }, "properties": { "blocks": { "items": [
              { "content": { "contentType": "latestArticlesBlock", "properties": { "heading": null, "count": null, "tag": " css " } } },
              { "content": { "contentType": "latestArticlesBlock", "properties": { "count": 500, "tag": "" } } },
              { "content": { "contentType": "latestArticlesBlock", "properties": { "count": 1, "tag": "C#" } } },
              { "content": { "contentType": "latestArticlesBlock", "properties": { "count": 1, "tag": "dotnet, css" } } },
              { "content": { "contentType": "latestArticlesBlock", "properties": { "count": 1, "tag": "!dotnet" } } }
            ] } } }
            """);
        var requested = new List<(string? Tag, int Count)>();

        await MapAsync(item, (tag, count, _) =>
        {
            requested.Add((tag, count));
            return Task.FromResult<ArticleSummary[]>([]);
        });

        Assert.Equal([("css", 6), (null, DeliveryApiMapper.MaxLatestArticles), ("C#", 1), (null, 1), (null, 1)], requested);
    }

    [Fact]
    public async Task Article_list_page_size_defaults_to_12_and_is_clamped()
    {
        var item = Parse<ApiContentResponseModel>("""
            { "id": "00000000-0000-0000-0000-000000000001", "contentType": "page", "name": "P", "route": { "path": "/p/" }, "properties": { "blocks": { "items": [
              { "content": { "contentType": "articleListBlock", "properties": { "heading": "All", "pageSize": null } } },
              { "content": { "contentType": "articleListBlock", "properties": { "pageSize": 500 } } },
              { "content": { "contentType": "articleListBlock", "properties": { "pageSize": 0 } } }
            ] } } }
            """);

        var page = await MapAsync(item);

        Assert.Equal(
            [new ArticleListBlock("All", 12), new ArticleListBlock(null, RequestValidation.MaxPageSize), new ArticleListBlock(null, 1)],
            page.Blocks.Cast<ArticleListBlock>());
    }

    [Fact]
    public async Task Article_without_publish_date_falls_back_to_create_date()
    {
        var item = Parse<ApiContentResponseModel>("""
            { "id": "00000000-0000-0000-0000-000000000001", "contentType": "article", "name": "A", "createDate": "2026-03-04T22:07:19.57Z",
              "route": { "path": "/articles/a/" }, "properties": { "body": null, "tags": null, "publishDate": null } }
            """);

        var article = Assert.IsType<Article>((await MapAsync(item)).Article);

        Assert.Equal(new DateOnly(2026, 3, 4), article.PublishDate);
        Assert.Empty(article.Tags);
        Assert.Equal(string.Empty, article.BodyHtml);
        Assert.Equal(1, article.ReadingTimeMinutes);
    }

    [Fact]
    public async Task Falls_back_to_name_when_title_missing_and_tolerates_missing_properties()
    {
        var item = Parse<ApiContentResponseModel>("""
            { "id": "00000000-0000-0000-0000-000000000001", "contentType": "page", "name": "Home", "updateDate": "2026-01-01T00:00:00Z",
              "route": { "path": "/" }, "properties": { "title": " " } }
            """);

        var page = await MapAsync(item);

        Assert.Equal("/", page.Path);
        Assert.Equal("Home", page.Title);
        Assert.Null(page.Summary);
        Assert.Null(page.Image);
        Assert.Null(page.Article);
        Assert.Empty(page.Blocks);
        Assert.Empty(page.Properties);
    }

    [Fact]
    public async Task Content_type_unknown_to_the_spec_passes_its_properties_through()
    {
        var item = Parse<ApiContentResponseModel>("""
            { "id": "00000000-0000-0000-0000-000000000001", "contentType": "event", "name": "Launch",
              "route": { "path": "/launch/" }, "properties": { "venue": "Online", "blocks": null } }
            """);

        var page = await MapAsync(item);

        Assert.IsType<UnknownApiContentResponseModel>(item);
        Assert.Equal(("event", "/launch", "Launch"), (page.ContentType, page.Path, page.Title));
        Assert.Equal(["venue", "blocks"], page.Properties.Keys);
        Assert.Equal("Online", page.Properties["venue"].GetString());
        Assert.Empty(page.Blocks);
    }

    [Theory]
    [InlineData("/about/", "/about")]
    [InlineData("about", "/about")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void Normalizes_paths(string? input, string expected) =>
        Assert.Equal(expected, DeliveryApiMapper.NormalizePath(input));
}
