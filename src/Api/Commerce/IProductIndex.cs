using Api.Contracts;

namespace Api.Commerce;

/// <summary>Port for the product search index (listing, facets, sorting). Implemented by <c>Commerce/Meilisearch</c>.</summary>
public interface IProductIndex
{
    Task EnsureIndexAsync(CancellationToken ct);

    Task<ProductSearchResult> SearchAsync(string query, int page, int pageSize, ProductFilters filters, ProductSort sort, CancellationToken ct);

    Task UpsertAsync(IEnumerable<Product> products, CancellationToken ct);

    /// <summary>Replaces the index atomically (like <see cref="Search.ISearchIndex.ReplaceAllAsync"/>); returns the count.</summary>
    Task<int> ReplaceAllAsync(IAsyncEnumerable<Product> products, CancellationToken ct);

    Task<long> CountAsync(CancellationToken ct);
}

/// <summary>Narrows a product search; <c>null</c> means any value.</summary>
/// <param name="MinRating">Whole stars, 1–5.</param>
public sealed record ProductFilters(string? Category = null, string? Brand = null, int? MinRating = null, bool? InStock = null);

/// <summary>Product orderings. Without a query, <see cref="Relevance"/> falls back to <see cref="Popular"/>.</summary>
public enum ProductSort
{
    Relevance,
    Popular,
    PriceAsc,
    PriceDesc,
    Rating,
    Newest,
}
