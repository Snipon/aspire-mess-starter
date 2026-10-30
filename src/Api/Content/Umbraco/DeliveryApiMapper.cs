using System.Text.Json;
using Api.Content.Umbraco.Delivery;
using Api.Contracts;
using Api.Endpoints;

namespace Api.Content.Umbraco;

/// <summary>Maps Umbraco Delivery API v2 content (models generated from its OpenAPI document) to contract types.</summary>
public sealed class DeliveryApiMapper(string publicBaseUrl, ImageUrlSigner signer, int[] renditionWidths)
{
    public const string ArticleType = "article";

    public const string BlogPostType = "blogPost";

    /// <summary>The blog post property holding the related article, expanded on item requests.</summary>
    public const string RelatedArticleProperty = "relatedArticle";

    /// <summary>The latestArticles and latestBlogPosts blocks' <c>count</c> has no default in Umbraco; this is the documented one.</summary>
    public const int DefaultLatestArticles = 6;

    public const int MaxLatestArticles = 50;

    /// <summary>The articleList block's <c>pageSize</c> default (also unset in Umbraco).</summary>
    public const int DefaultArticleListPageSize = 12;

    /// <summary>Resolves the articles of a latestArticles block (newest first, optionally only those with a tag).</summary>
    public delegate Task<ArticleSummary[]> LatestArticlesResolver(string? tag, int count, CancellationToken ct);

    /// <summary>Resolves the posts of a latestBlogPosts block (newest first).</summary>
    public delegate Task<BlogPostSummary[]> LatestBlogPostsResolver(int count, CancellationToken ct);

    private readonly string _publicBaseUrl = publicBaseUrl.TrimEnd('/');

    /// <param name="latestArticles">Fills latestArticles blocks; when <c>null</c> they are left empty (e.g. for indexing).</param>
    /// <param name="latestBlogPosts">Fills latestBlogPosts blocks; when <c>null</c> they are left empty.</param>
    public async Task<Page> ToPageAsync(
        ApiContentResponseModel item, string? culture, LatestArticlesResolver? latestArticles, LatestBlogPostsResolver? latestBlogPosts,
        CancellationToken ct)
    {
        var name = item.Name ?? string.Empty;
        string? title = null;
        string? summary = null;
        Image? image = null;
        ApiBlockListModel? blocks = null;
        Article? article = null;
        BlogPost? blogPost = null;
        Dictionary<string, JsonElement>? extra = null;

        switch (item)
        {
            case ArticleContentResponseModel { Properties: var props }:
                var body = MarkdownText.Parse(props?.Body);
                title = props?.Title;
                summary = MarkdownText.Excerpt(body);
                image = GetImage(props?.Image);
                article = new Article(
                    GetPublishDate(item.CreateDate, props?.PublishDate),
                    GetTags(props),
                    MarkdownText.ToHtml(body, ToPublicMediaUrl),
                    MarkdownText.ReadingTimeMinutes(body),
                    ProductSku: string.IsNullOrWhiteSpace(props?.Product) ? null : props.Product.Trim());
                extra = props?.Additional;
                break;
            case BlogPostContentResponseModel { Properties: var props }:
                var postBody = MarkdownText.Parse(props?.Body);
                title = props?.Title;
                summary = MarkdownText.Excerpt(postBody);
                image = GetImage(props?.Image);
                blogPost = new BlogPost(
                    GetPublishDate(item.CreateDate, props?.PublishDate),
                    MarkdownText.ToHtml(postBody, ToPublicMediaUrl),
                    MarkdownText.ReadingTimeMinutes(postBody),
                    props?.RelatedArticle is ArticleContentModel related ? ToArticleSummary(related) : null);
                extra = props?.Additional;
                break;
            case PageContentResponseModel { Properties: var props }:
                (title, blocks, extra) = (props?.Title, props?.Blocks, props?.Additional);
                break;
            case SiteContentResponseModel { Properties: var props }:
                (blocks, extra) = (props?.Blocks, props?.Additional);
                break;
            case UnknownApiContentResponseModel { Properties: { ValueKind: JsonValueKind.Object } props }:
                // A content type the spec does not know yet: its properties pass through raw.
                extra = props.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
                break;
        }

        return new Page(
            Id: item.Id.ToString("D"),
            ContentType: item.ContentType,
            Path: GetPath(item),
            Name: name,
            Title: string.IsNullOrWhiteSpace(title) ? name : title,
            Summary: summary,
            Image: image,
            Blocks: await GetBlocksAsync(blocks, latestArticles, latestBlogPosts, ct),
            Article: article,
            BlogPost: blogPost,
            Culture: culture,
            UpdatedAt: item.UpdateDate,
            Properties: extra ?? []);
    }

    public ArticleSummary ToArticleSummary(ApiContentResponseModel item) =>
        ToArticleSummary(item.Id, item.Route, item.Name, item.CreateDate, (item as ArticleContentResponseModel)?.Properties);

    /// <summary>An article referenced from another item (e.g. an expanded content picker).</summary>
    private ArticleSummary ToArticleSummary(ArticleContentModel item) =>
        ToArticleSummary(item.Id, item.Route, item.Name, item.CreateDate, item.Properties);

    private ArticleSummary ToArticleSummary(
        Guid id, ApiContentRouteModel? route, string? name, DateTimeOffset createDate, ArticleContentPropertiesModel? props) => new(
            Id: id.ToString("D"),
            Path: NormalizePath(route?.Path),
            Title: string.IsNullOrWhiteSpace(props?.Title) ? name ?? string.Empty : props.Title,
            Summary: MarkdownText.Excerpt(MarkdownText.Parse(props?.Body)),
            Image: GetImage(props?.Image),
            PublishDate: GetPublishDate(createDate, props?.PublishDate),
            Tags: GetTags(props));

    public BlogPostSummary ToBlogPostSummary(ApiContentResponseModel item)
    {
        var props = (item as BlogPostContentResponseModel)?.Properties;
        var name = item.Name ?? string.Empty;
        return new BlogPostSummary(
            Id: item.Id.ToString("D"),
            Path: GetPath(item),
            Title: string.IsNullOrWhiteSpace(props?.Title) ? name : props.Title,
            Summary: MarkdownText.Excerpt(MarkdownText.Parse(props?.Body)),
            Image: GetImage(props?.Image),
            PublishDate: GetPublishDate(item.CreateDate, props?.PublishDate));
    }

    public static string[] GetTags(ApiContentResponseModel item) => GetTags((item as ArticleContentResponseModel)?.Properties);

    public static string GetPath(ApiContentResponseModel item) => NormalizePath(item.Route?.Path);

    /// <summary>Normalises to a leading slash and no trailing slash (root is <c>/</c>).</summary>
    public static string NormalizePath(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim().Trim('/');
        return "/" + trimmed;
    }

    public string ToAbsoluteUrl(string url) => IsHttpUrl(url) ? url : _publicBaseUrl + "/" + url.TrimStart('/');

    // Media is served by the CMS; other relative URLs are site paths handled by the web front end.
    private string ToPublicMediaUrl(string url) =>
        url.StartsWith("/media/", StringComparison.Ordinal) ? _publicBaseUrl + url : url;

    // Not Uri.TryCreate(..., Absolute): on Linux "/media/x.jpg" parses as an absolute file:// URI.
    private static bool IsHttpUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private async Task<Block[]> GetBlocksAsync(
        ApiBlockListModel? list, LatestArticlesResolver? latestArticles, LatestBlogPostsResolver? latestBlogPosts, CancellationToken ct)
    {
        var blocks = new List<Block>();
        foreach (var entry in list?.Items ?? [])
        {
            Block? block = entry.Content switch
            {
                HeroBlockElementModel { Properties: var p } => new HeroBlock(
                    p?.Heading ?? string.Empty,
                    p?.Subheading,
                    GetImage(p?.Image),
                    GetLink(p?.CtaLink, title: p?.CtaLabel),
                    GetVideo(p?.Video)),
                CtaBlockElementModel { Properties: var p } => new CtaBlock(
                    p?.Heading ?? string.Empty,
                    p?.Text,
                    GetLink(p?.Link),
                    p?.Style == "secondary" ? CtaStyle.Secondary : CtaStyle.Primary),
                LatestArticlesBlockElementModel { Properties: var p } => await GetLatestArticlesAsync(p, latestArticles, ct),
                ArticleListBlockElementModel { Properties: var p } => new ArticleListBlock(
                    p?.Heading,
                    Math.Clamp(p?.PageSize ?? DefaultArticleListPageSize, 1, RequestValidation.MaxPageSize)),
                LatestBlogPostsBlockElementModel { Properties: var p } => new LatestBlogPostsBlock(
                    p?.Heading,
                    latestBlogPosts is null
                        ? []
                        : await latestBlogPosts(Math.Clamp(p?.Count ?? DefaultLatestArticles, 1, MaxLatestArticles), ct)),
                BlogPostListBlockElementModel { Properties: var p } => new BlogPostListBlock(
                    p?.Heading,
                    Math.Clamp(p?.PageSize ?? DefaultArticleListPageSize, 1, RequestValidation.MaxPageSize)),
                _ => null, // Unknown block types are skipped, so new CMS blocks never break existing clients.
            };
            if (block is not null)
            {
                blocks.Add(block);
            }
        }

        return blocks.ToArray();
    }

    private static async Task<LatestArticlesBlock> GetLatestArticlesAsync(
        LatestArticlesBlockElementPropertiesModel? props, LatestArticlesResolver? latestArticles, CancellationToken ct)
    {
        // A free-text Textstring: anything but a single tag (e.g. "a, b" or "!a" in tag filter syntax) is ignored,
        // the same rule the API applies to the tag its "All articles" link leads to.
        var tag = string.IsNullOrWhiteSpace(props?.Tag) ? null : props.Tag.Trim();
        tag = RequestValidation.IsValidTag(tag) ? tag : null;
        var count = Math.Clamp(props?.Count ?? DefaultLatestArticles, 1, MaxLatestArticles);
        var articles = latestArticles is null ? [] : await latestArticles(tag, count, ct);
        return new LatestArticlesBlock(props?.Heading, tag, articles);
    }

    /// <summary>Maps the first link of a Multi URL Picker.</summary>
    private Link? GetLink(ApiLinkModel[]? links, string? title = null)
    {
        if (links is not [var link, ..])
        {
            return null;
        }

        var url = link.LinkType switch
        {
            "Content" => link.Route is null ? null : NormalizePath(link.Route.Path),
            "Media" => link.Url is { Length: > 0 } media ? ToAbsoluteUrl(media) : null,
            _ => link.Url,
        };
        if (string.IsNullOrWhiteSpace(url) || !MarkdownText.IsSafeUrl(url))
        {
            return null;
        }

        // Umbraco keeps "?query" / "#anchor" apart from the URL.
        return new Link(url + link.QueryString, string.IsNullOrWhiteSpace(title) ? link.Title : title, link.Target);
    }

    private static DateOnly GetPublishDate(DateTimeOffset createDate, DateTimeOffset? publishDate) =>
        // Date picker values have no offset ("2026-10-28T00:00:00"), so take the date as written. Fall back to the
        // create date like the CMS's publishDate sort does.
        publishDate is { } date ? DateOnly.FromDateTime(date.DateTime) : DateOnly.FromDateTime(createDate.UtcDateTime);

    private static string[] GetTags(ArticleContentPropertiesModel? props) =>
        props?.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? [];

    private Image? GetImage(ApiMediaWithCropsModel[]? media)
    {
        if (media is not [{ Url: { Length: > 0 } url } first, ..])
        {
            return null;
        }

        var focalPoint = first.FocalPoint is { } fp ? new FocalPoint(fp.Left, fp.Top) : null;

        // Only CMS-hosted (relative) media can be resized by the CMS's ImageSharp middleware.
        var srcset = !IsHttpUrl(url)
            ? renditionWidths
                .Where(w => first.Width is null || w <= first.Width)
                .Select(w => new ImageRendition(w, ToAbsoluteUrl(signer.Resize(url, w))))
                .ToArray()
            : [];

        // The Image media type has no alt text property, so the media name serves as alt text.
        return new Image(ToAbsoluteUrl(url), first.Name, first.Width, first.Height, focalPoint, srcset);
    }

    /// <summary>The first picked item if it is a Video media item in a format browsers play; anything else is null.</summary>
    private Video? GetVideo(ApiMediaWithCropsModel[]? media)
    {
        if (media is not [UmbracoMediaVideoMediaWithCropsModel { Url: { Length: > 0 } url } video, ..])
        {
            return null;
        }

        var mimeType = (video.Extension ?? Path.GetExtension(url).TrimStart('.')).ToLowerInvariant() switch
        {
            "mp4" => "video/mp4",
            "webm" => "video/webm",
            "ogv" or "ogg" => "video/ogg",
            _ => null,
        };
        return mimeType is null ? null : new Video(ToAbsoluteUrl(url), mimeType);
    }
}
