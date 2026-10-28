using Api.Contracts;

namespace Api.Content;

/// <summary>Port for reading published content. Implemented by a CMS adapter (see <c>Content/Umbraco</c>).</summary>
public interface IContentSource
{
    Task<Page?> GetByPathAsync(string path, string? culture, CancellationToken ct);

    Task<Page?> GetByIdAsync(string id, string? culture, CancellationToken ct);

    Task<NavItem[]> GetNavigationAsync(string? culture, CancellationToken ct);

    IAsyncEnumerable<SearchDocument> GetAllForIndexingAsync(CancellationToken ct);
}
