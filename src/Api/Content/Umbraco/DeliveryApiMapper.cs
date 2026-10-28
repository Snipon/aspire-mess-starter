using System.Text.Json;
using Api.Contracts;
using Api.Endpoints;

namespace Api.Content.Umbraco;

/// <summary>Maps Umbraco Delivery API v2 content JSON to contract types.</summary>
public sealed class DeliveryApiMapper(string publicBaseUrl, ImageUrlSigner signer, int[] renditionWidths)
{
    public const string ArticleType = "article";

    /// <summary>The latestArticles block's <c>count</c> has no default in Umbraco; this is the documented one.</summary>
    public const int DefaultLatestArticles = 6;

    public const int MaxLatestArticles = 50;

    /// <summary>Resolves the articles of a latestArticles block (newest first, optionally only those with a tag).</summary>
    public delegate Task<ArticleSummary[]> LatestArticlesResolver(string? tag, int count, CancellationToken ct);

    private static readonly HashSet<string> MappedProperties = ["title", "summary", "image", "blocks", "body", "tags", "publishDate"];

    private readonly string _publicBaseUrl = publicBaseUrl.TrimEnd('/');

    /// <param name="latestArticles">Fills latestArticles blocks; when <c>null</c> they are left empty (e.g. for indexing).</param>
    public async Task<Page> ToPageAsync(JsonElement item, string? culture, LatestArticlesResolver? latestArticles, CancellationToken ct)
    {
        var name = item.GetProperty("name").GetString() ?? string.Empty;
        var contentType = item.GetProperty("contentType").GetString()!;
        var properties = GetProperties(item);

        var extra = new Dictionary<string, JsonElement>();
        if (properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (!MappedProperties.Contains(property.Name))
                {
                    extra[property.Name] = property.Value.Clone();
                }
            }
        }

        Article? article = null;
        var summary = GetString(properties, "summary");
        if (contentType == ArticleType)
        {
            var body = MarkdownText.Parse(GetString(properties, "body"));
            article = new Article(
                GetPublishDate(item, properties),
                GetTagsOf(properties),
                MarkdownText.ToHtml(body, ToPublicMediaUrl),
                MarkdownText.ReadingTimeMinutes(body));
            summary ??= MarkdownText.Excerpt(body);
        }

        return new Page(
            Id: item.GetProperty("id").GetString()!,
            ContentType: contentType,
            Path: GetPath(item),
            Name: name,
            Title: GetTitle(properties, name),
            Summary: summary,
            Image: GetImage(properties, "image"),
            Blocks: await GetBlocksAsync(properties, latestArticles, ct),
            Article: article,
            Culture: culture,
            UpdatedAt: item.TryGetProperty("updateDate", out var updated) ? updated.GetDateTimeOffset() : default,
            Properties: extra);
    }

    public ArticleSummary ToArticleSummary(JsonElement item)
    {
        var properties = GetProperties(item);
        return new ArticleSummary(
            Id: item.GetProperty("id").GetString()!,
            Path: GetPath(item),
            Title: GetTitle(properties, item.GetProperty("name").GetString() ?? string.Empty),
            Summary: GetString(properties, "summary") ?? MarkdownText.Excerpt(MarkdownText.Parse(GetString(properties, "body"))),
            Image: GetImage(properties, "image"),
            PublishDate: GetPublishDate(item, properties),
            Tags: GetTagsOf(properties));
    }

    public static string[] GetTags(JsonElement item) => GetTagsOf(GetProperties(item));

    public static string GetPath(JsonElement item)
    {
        var path = item.TryGetProperty("route", out var route) && route.TryGetProperty("path", out var p)
            ? p.GetString()
            : null;
        return NormalizePath(path);
    }

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

    private static JsonElement GetProperties(JsonElement item) =>
        item.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object ? props : default;

    private static string GetTitle(JsonElement properties, string name)
    {
        var title = GetString(properties, "title");
        return string.IsNullOrWhiteSpace(title) ? name : title;
    }

    private async Task<Block[]> GetBlocksAsync(JsonElement properties, LatestArticlesResolver? latestArticles, CancellationToken ct)
    {
        // Block List: { "items": [ { "content": { "contentType", "id", "properties" }, "settings": ... } ] }
        if (properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty("blocks", out var value)
            || value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var blocks = new List<Block>();
        foreach (var entry in items.EnumerateArray())
        {
            if (!entry.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var props = GetProperties(content);
            Block? block = GetString(content, "contentType") switch
            {
                "heroBlock" => new HeroBlock(
                    GetString(props, "heading") ?? string.Empty,
                    GetString(props, "subheading"),
                    GetImage(props, "image"),
                    GetLink(props, "ctaLink", title: GetString(props, "ctaLabel"))),
                "ctaBlock" => new CtaBlock(
                    GetString(props, "heading") ?? string.Empty,
                    GetString(props, "text"),
                    GetLink(props, "link"),
                    GetString(props, "style") == "secondary" ? CtaStyle.Secondary : CtaStyle.Primary),
                "latestArticlesBlock" => await GetLatestArticlesAsync(props, latestArticles, ct),
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
        JsonElement props, LatestArticlesResolver? latestArticles, CancellationToken ct)
    {
        var tag = GetString(props, "tag");
        // A free-text Textstring: anything but a single tag (e.g. "a, b" or "!a" in tag filter syntax) is ignored,
        // the same rule the API applies to the tag its "All articles" link leads to.
        tag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
        tag = RequestValidation.IsValidTag(tag) ? tag : null;
        var count = Math.Clamp(GetInt(props, "count") ?? DefaultLatestArticles, 1, MaxLatestArticles);
        var articles = latestArticles is null ? [] : await latestArticles(tag, count, ct);
        return new LatestArticlesBlock(GetString(props, "heading"), tag, articles);
    }

    /// <summary>Maps the first link of a Multi URL Picker (an array, or <c>null</c> when empty).</summary>
    private Link? GetLink(JsonElement properties, string name, string? title = null)
    {
        var link = FirstObject(properties, name);
        if (link.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var url = GetString(link, "linkType") switch
        {
            "Content" => link.TryGetProperty("route", out var route) && route.ValueKind == JsonValueKind.Object
                ? NormalizePath(GetString(route, "path"))
                : null,
            "Media" => GetString(link, "url") is { Length: > 0 } media ? ToAbsoluteUrl(media) : null,
            _ => GetString(link, "url"),
        };
        if (string.IsNullOrWhiteSpace(url) || !MarkdownText.IsSafeUrl(url))
        {
            return null;
        }

        // Umbraco keeps "?query" / "#anchor" apart from the URL.
        return new Link(url + GetString(link, "queryString"), string.IsNullOrWhiteSpace(title) ? GetString(link, "title") : title,
            GetString(link, "target"));
    }

    private static DateOnly GetPublishDate(JsonElement item, JsonElement properties)
    {
        // Date picker values have no offset ("2026-10-28T00:00:00"). Fall back to the create date like the CMS's
        // publishDate sort does.
        if (properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty("publishDate", out var value)
            && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTime(out var date))
        {
            return DateOnly.FromDateTime(date);
        }

        return item.TryGetProperty("createDate", out var created) && created.TryGetDateTimeOffset(out var createDate)
            ? DateOnly.FromDateTime(createDate.UtcDateTime)
            : default;
    }

    private static string[] GetTagsOf(JsonElement properties) =>
        properties.ValueKind == JsonValueKind.Object
        && properties.TryGetProperty("tags", out var tags)
        && tags.ValueKind == JsonValueKind.Array
            ? tags.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                .Select(t => t.GetString()!)
                .ToArray()
            : [];

    private static string? GetString(JsonElement properties, string name) =>
        properties.ValueKind == JsonValueKind.Object
        && properties.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Pickers return an array of items (or a single item, depending on configuration).</summary>
    private static JsonElement FirstObject(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(name, out var value))
        {
            return default;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Array when value.GetArrayLength() > 0 => value[0],
            JsonValueKind.Object => value,
            _ => default,
        };
    }

    private Image? GetImage(JsonElement properties, string name)
    {
        var media = FirstObject(properties, name);
        if (media.ValueKind != JsonValueKind.Object
            || !media.TryGetProperty("url", out var urlElement)
            || urlElement.GetString() is not { Length: > 0 } url)
        {
            return null;
        }

        var width = GetInt(media, "width");
        var height = GetInt(media, "height");

        FocalPoint? focalPoint = null;
        if (media.TryGetProperty("focalPoint", out var fp) && fp.ValueKind == JsonValueKind.Object)
        {
            focalPoint = new FocalPoint(fp.GetProperty("left").GetDouble(), fp.GetProperty("top").GetDouble());
        }

        var alt = media.TryGetProperty("properties", out var mediaProps)
            ? GetString(mediaProps, "altText") ?? GetString(mediaProps, "alt")
            : null;
        alt ??= media.TryGetProperty("name", out var mediaName) ? mediaName.GetString() : null;

        // Only CMS-hosted (relative) media can be resized by the CMS's ImageSharp middleware.
        var srcset = !IsHttpUrl(url)
            ? renditionWidths
                .Where(w => width is null || w <= width)
                .Select(w => new ImageRendition(w, ToAbsoluteUrl(signer.Resize(url, w))))
                .ToArray()
            : [];

        return new Image(ToAbsoluteUrl(url), alt, width, height, focalPoint, srcset);
    }

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
