using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Api.Contracts;

namespace Api.Content.Umbraco;

/// <summary><see cref="IContentSource"/> backed by the Umbraco Delivery API v2.</summary>
public sealed class UmbracoContentSource(HttpClient http, DeliveryApiMapper mapper) : IContentSource
{
    internal const string ContentApi = "umbraco/delivery/api/v2/content";
    private const int PageSize = 100;

    public Task<Page?> GetByPathAsync(string path, string? culture, CancellationToken ct)
    {
        // item/{*path}: an empty path resolves to the root ("/").
        var segments = DeliveryApiMapper.NormalizePath(path).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Dot segments survive Uri.EscapeDataString and are collapsed by Uri, escaping the content route (with the
        // Api-Key attached), so they never resolve to content.
        if (segments.Any(s => s is "." or ".."))
        {
            return Task.FromResult<Page?>(null);
        }

        return GetItemAsync($"{ContentApi}/item/{string.Join('/', segments.Select(Uri.EscapeDataString))}", culture, ct);
    }

    public Task<Page?> GetByIdAsync(string id, string? culture, CancellationToken ct) =>
        Guid.TryParse(id, out var key)
            ? GetItemAsync($"{ContentApi}/item/{key:D}", culture, ct)
            : Task.FromResult<Page?>(null);

    public async Task<NavItem[]> GetNavigationAsync(string? culture, CancellationToken ct)
    {
        var nodes = new List<NavNode>();
        await foreach (var item in QueryAllAsync($"filter=contentType:!{DeliveryApiMapper.ArticleType}", culture, ct))
        {
            nodes.Add(new NavNode(item.GetProperty("id").GetString()!, item.GetProperty("name").GetString() ?? string.Empty,
                DeliveryApiMapper.GetPath(item)));
        }

        // Items arrive in sort order, so appending to the parent keeps sibling order.
        var byPath = new Dictionary<string, NavNode>();
        foreach (var node in nodes)
        {
            byPath.TryAdd(node.Path, node);
        }

        var roots = new List<NavNode>();
        foreach (var node in nodes)
        {
            if (ParentPath(node.Path) is { } parentPath && byPath.TryGetValue(parentPath, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return roots.Select(n => n.ToNavItem()).ToArray();
    }

    public async IAsyncEnumerable<SearchDocument> GetAllForIndexingAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // latestArticles blocks are left unresolved: their articles are indexed in their own right.
        await foreach (var item in QueryAllAsync(filter: null, culture: null, ct))
        {
            yield return SearchDocument.FromPage(await mapper.ToPageAsync(item, culture: null, latestArticles: null, ct));
        }
    }

    public async Task<Paged<ArticleSummary>> GetArticlesAsync(string? tag, int page, int pageSize, string? culture, CancellationToken ct)
    {
        using var request = CreateRequest(
            $"{ContentApi}?{ArticlesFilter(tag)}&sort=publishDate:desc&skip={(page - 1) * pageSize}&take={pageSize}", culture);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        var items = json.RootElement.GetProperty("items").EnumerateArray().Select(mapper.ToArticleSummary).ToArray();
        return new Paged<ArticleSummary>(page, pageSize, json.RootElement.GetProperty("total").GetInt32(), items);
    }

    public async Task<TagCount[]> GetTagsAsync(string? culture, CancellationToken ct)
    {
        // The Delivery API has no tag listing, so count over all articles (fetching only their tags property).
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await foreach (var item in QueryAllAsync($"{ArticlesFilter(tag: null)}&fields=properties[tags]", culture, ct))
        {
            foreach (var tag in DeliveryApiMapper.GetTags(item).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }

        return counts
            .OrderByDescending(c => c.Value)
            .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .Select(c => new TagCount(c.Key, c.Value))
            .ToArray();
    }

    // tag: is a CMS query extension (src/Cms/Headless/ArticleQueryHandlers.cs); ',' would mean "any of".
    private static string ArticlesFilter(string? tag) =>
        $"filter=contentType:{DeliveryApiMapper.ArticleType}" + (tag is null ? null : $"&filter=tag:{Uri.EscapeDataString(tag)}");

    private async Task<Page?> GetItemAsync(string uri, string? culture, CancellationToken ct)
    {
        using var request = CreateRequest(uri, culture);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MovedPermanently)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return await mapper.ToPageAsync(json.RootElement, culture, LatestArticlesAsync, ct);

        async Task<ArticleSummary[]> LatestArticlesAsync(string? tag, int count, CancellationToken token) =>
            (await GetArticlesAsync(tag, page: 1, pageSize: count, culture, token)).Items;
    }

    /// <summary>
    /// Pages through all published content matching <paramref name="filter"/> (no <c>fetch</c> selector returns
    /// everything), in sort order.
    /// </summary>
    private async IAsyncEnumerable<JsonElement> QueryAllAsync(string? filter, string? culture, [EnumeratorCancellation] CancellationToken ct)
    {
        var prefix = filter is null ? null : filter + "&";
        for (var skip = 0; ; skip += PageSize)
        {
            using var request = CreateRequest($"{ContentApi}?{prefix}sort=sortOrder:asc&skip={skip}&take={PageSize}", culture);
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var items = json.RootElement.GetProperty("items");
            foreach (var item in items.EnumerateArray())
            {
                yield return item.Clone();
            }

            if (items.GetArrayLength() < PageSize || skip + PageSize >= json.RootElement.GetProperty("total").GetInt32())
            {
                yield break;
            }
        }
    }

    private static HttpRequestMessage CreateRequest(string uri, string? culture)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(culture) && !request.Headers.AcceptLanguage.TryParseAdd(culture))
        {
            throw new ArgumentException($"Invalid culture '{culture}'.", nameof(culture));
        }

        return request;
    }

    private static string? ParentPath(string path)
    {
        if (path == "/")
        {
            return null;
        }

        var index = path.LastIndexOf('/');
        return index <= 0 ? "/" : path[..index];
    }

    private sealed record NavNode(string Id, string Name, string Path)
    {
        public List<NavNode> Children { get; } = [];

        public NavItem ToNavItem() => new(Id, Name, Path, Children.Select(c => c.ToNavItem()).ToArray());
    }
}
