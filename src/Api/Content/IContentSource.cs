using Api.Contracts;

namespace Api.Content;

/// <summary>Port for reading published content. Implemented by a CMS adapter (see <c>Content/Umbraco</c>).</summary>
public interface IContentSource
{
    Task<Page?> GetByPathAsync(string path, string? culture, CancellationToken ct);

    Task<Page?> GetByIdAsync(string id, string? culture, CancellationToken ct);

    /// <summary>The latest saved version of a page, unpublished changes included; callers must restrict access.</summary>
    Task<Page?> GetPreviewByIdAsync(string id, string? culture, CancellationToken ct);

    /// <summary>The site tree without articles and blog posts (they are listed by <see cref="GetArticlesAsync"/> and search).</summary>
    Task<NavItem[]> GetNavigationAsync(string? culture, CancellationToken ct);

    /// <summary>Published articles, newest publish date first, optionally only those tagged <paramref name="tag"/>.</summary>
    Task<Paged<ArticleSummary>> GetArticlesAsync(string? tag, int page, int pageSize, string? culture, CancellationToken ct);

    /// <summary>All article tags with the number of published articles that carry them, most used first.</summary>
    Task<TagCount[]> GetTagsAsync(string? culture, CancellationToken ct);

    IAsyncEnumerable<SearchDocument> GetAllForIndexingAsync(CancellationToken ct);
}
