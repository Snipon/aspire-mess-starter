using System.Globalization;
using Api.Contracts;
using Meilisearch;
using Meilisearch.QueryParameters;
using MeiliIndex = Meilisearch.Index;

namespace Api.Commerce.Meilisearch;

/// <summary><see cref="IProductIndex"/> backed by a Meilisearch index named <c>products</c> (SKU as primary key).</summary>
public sealed class MeilisearchProductIndex(MeilisearchClient client) : IProductIndex
{
    public const string IndexName = "products";

    private const string IndexNotFound = "index_not_found";
    private const int BatchSize = 500;

    private const string CategoryFacet = "category";
    private const string BrandFacet = "brand";
    private const string RatingFacet = "ratingFloor";
    private const string AvailabilityFacet = "availability";
    private const string InStock = "inStock";
    private const string OutOfStock = "outOfStock";

    private MeiliIndex Index => client.Index(IndexName);

    public Task EnsureIndexAsync(CancellationToken ct) => CreateAndConfigureAsync(IndexName, ct);

    private async Task CreateAndConfigureAsync(string uid, CancellationToken ct)
    {
        var create = await client.CreateIndexAsync(uid, "sku", ct);
        await client.WaitForTaskAsync(create.TaskUid, cancellationToken: ct); // fails harmlessly if it already exists

        var settings = await client.Index(uid).UpdateSettingsAsync(new Settings
        {
            SearchableAttributes = ["title", "brand", "category", "sku", "specs", "description"],
            FilterableAttributes = [CategoryFacet, BrandFacet, RatingFacet, AvailabilityFacet, "price"],
            SortableAttributes = ["price", "rating", "unitsSold", "releaseDate"],
        }, ct);
        await WaitAsync(settings.TaskUid, ct);
    }

    public async Task<ProductSearchResult> SearchAsync(
        string query, int page, int pageSize, ProductFilters filters, ProductSort sort, CancellationToken ct)
    {
        var effectiveSort = sort == ProductSort.Relevance && query.Length == 0 ? ProductSort.Popular : sort;
        var main = SearchOrNullAsync(query, new SearchQuery
        {
            Page = page,
            HitsPerPage = pageSize,
            Filter = Filter(filters),
            Facets = [CategoryFacet, BrandFacet, RatingFacet, AvailabilityFacet],
            Sort = SortExpression(effectiveSort),
        }, ct);

        // Like the content search: a filtered facet is counted again without its own filter.
        var categories = filters.Category is null ? main : FacetAsync(query, CategoryFacet, filters with { Category = null }, ct);
        var brands = filters.Brand is null ? main : FacetAsync(query, BrandFacet, filters with { Brand = null }, ct);
        var ratings = filters.MinRating is null ? main : FacetAsync(query, RatingFacet, filters with { MinRating = null }, ct);
        var availability = filters.InStock is null ? main : FacetAsync(query, AvailabilityFacet, filters with { InStock = null }, ct);
        await Task.WhenAll(main, categories, brands, ratings, availability);

        var sortName = SortName(sort);
        if (await main is not { } result)
        {
            return new ProductSearchResult(query, sortName, page, pageSize, 0, [], ProductFacets.Empty);
        }

        var facets = new ProductFacets(
            Counts(await categories, CategoryFacet),
            Counts(await brands, BrandFacet),
            [.. Counts(await ratings, RatingFacet).OrderByDescending(c => c.Value, StringComparer.Ordinal)],
            Counts(await availability, AvailabilityFacet));
        return new ProductSearchResult(query, sortName, page, pageSize, result.TotalHits, [.. result.Hits.Select(h => h.ToSummary())], facets);
    }

    public static string SortName(ProductSort sort) => sort switch
    {
        ProductSort.Popular => "popular",
        ProductSort.PriceAsc => "price-asc",
        ProductSort.PriceDesc => "price-desc",
        ProductSort.Rating => "rating",
        ProductSort.Newest => "newest",
        _ => "relevance",
    };

    private static string[]? SortExpression(ProductSort sort) => sort switch
    {
        ProductSort.Popular => ["unitsSold:desc"],
        ProductSort.PriceAsc => ["price:asc"],
        ProductSort.PriceDesc => ["price:desc"],
        ProductSort.Rating => ["rating:desc", "unitsSold:desc"],
        ProductSort.Newest => ["releaseDate:desc"],
        _ => null,
    };

    private Task<PaginatedSearchResult<IndexedProduct>?> FacetAsync(string query, string facet, ProductFilters filters, CancellationToken ct) =>
        SearchOrNullAsync(query, new SearchQuery { HitsPerPage = 0, Filter = Filter(filters), Facets = [facet] }, ct);

    private async Task<PaginatedSearchResult<IndexedProduct>?> SearchOrNullAsync(string query, SearchQuery parameters, CancellationToken ct)
    {
        try
        {
            return (PaginatedSearchResult<IndexedProduct>)await Index.SearchAsync<IndexedProduct>(query, parameters, ct);
        }
        catch (MeilisearchApiError e) when (e.Code == IndexNotFound)
        {
            return null;
        }
    }

    private static string? Filter(ProductFilters filters)
    {
        var clauses = new List<string>();
        if (filters.Category is not null)
        {
            clauses.Add($"{CategoryFacet} = {Quote(filters.Category)}");
        }

        if (filters.Brand is not null)
        {
            clauses.Add($"{BrandFacet} = {Quote(filters.Brand)}");
        }

        if (filters.MinRating is not null)
        {
            clauses.Add($"{RatingFacet} >= {filters.MinRating}");
        }

        if (filters.InStock is not null)
        {
            clauses.Add($"{AvailabilityFacet} = {(filters.InStock.Value ? InStock : OutOfStock)}");
        }

        return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
    }

    private static FacetCount[] Counts(PaginatedSearchResult<IndexedProduct>? result, string facet) =>
        result?.FacetDistribution?.GetValueOrDefault(facet) is { } counts
            ? [.. counts
                .OrderByDescending(c => c.Value)
                .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .Select(c => new FacetCount(c.Key, c.Value))]
            : [];

    public async Task UpsertAsync(IEnumerable<Product> products, CancellationToken ct)
    {
        var task = await Index.AddDocumentsAsync(products.Select(IndexedProduct.From), "sku", ct);
        await WaitAsync(task.TaskUid, ct);
    }

    public async Task<int> ReplaceAllAsync(IAsyncEnumerable<Product> products, CancellationToken ct)
    {
        // Same staging-and-swap approach as MeilisearchSearchIndex.ReplaceAllAsync.
        var staging = $"{IndexName}_staging_{Guid.NewGuid():N}";
        var count = 0;
        try
        {
            await CreateAndConfigureAsync(staging, ct);
            var batch = new List<IndexedProduct>(BatchSize);
            await foreach (var product in products.WithCancellation(ct))
            {
                batch.Add(IndexedProduct.From(product));
                if (batch.Count == BatchSize)
                {
                    count += await AddAsync(staging, batch, ct);
                }
            }

            if (batch.Count > 0)
            {
                count += await AddAsync(staging, batch, ct);
            }

            if (count == 0)
            {
                return 0; // Keep the live index: an empty source almost always means "not ready yet".
            }

            await EnsureIndexAsync(ct);
            var swap = await client.SwapIndexesAsync([new IndexSwap(IndexName, staging, rename: false)], ct);
            await WaitAsync(swap.TaskUid, ct);
            return count;
        }
        finally
        {
            try
            {
                await client.DeleteIndexAsync(staging, CancellationToken.None);
            }
            catch (MeilisearchApiError)
            {
                // Best effort.
            }
        }
    }

    public async Task<long> CountAsync(CancellationToken ct)
    {
        try
        {
            return (await Index.GetStatsAsync(ct)).NumberOfDocuments;
        }
        catch (MeilisearchApiError e) when (e.Code == IndexNotFound)
        {
            return 0;
        }
    }

    private async Task<int> AddAsync(string uid, List<IndexedProduct> batch, CancellationToken ct)
    {
        var task = await client.Index(uid).AddDocumentsAsync(batch, "sku", ct);
        await WaitAsync(task.TaskUid, ct);
        var added = batch.Count;
        batch.Clear();
        return added;
    }

    private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private async Task WaitAsync(int taskUid, CancellationToken ct)
    {
        var task = await client.WaitForTaskAsync(taskUid, cancellationToken: ct);
        if (task.Status == TaskInfoStatus.Failed)
        {
            throw new InvalidOperationException($"Meilisearch task {taskUid} failed: {task.Error?.GetValueOrDefault("message")}");
        }
    }

    /// <param name="Specs">"Label: value unit" lines, so searches like "32 GB" or "OLED" match.</param>
    /// <param name="ReleaseDate">Unix seconds (sortable).</param>
    private sealed record IndexedProduct(
        string Sku, string Slug, string Path, string Title, string? Description, string Category, string Brand, Image? Image,
        decimal Price, string Currency, int Stock, string Availability, double Rating, int RatingFloor, int ReviewCount, int UnitsSold,
        long? ReleaseDate, string[] Specs)
    {
        public static IndexedProduct From(Product p) => new(
            p.Sku, p.Slug, p.Path, p.Title, p.Summary, p.Category, p.Brand, p.Image, p.Price.Amount, p.Price.Currency, p.Stock,
            p.Stock > 0 ? InStock : OutOfStock, p.Stats.Rating, (int)Math.Floor(p.Stats.Rating), p.Stats.ReviewCount, p.Stats.UnitsSold,
            p.ReleaseDate is { } date ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds() : null,
            [.. p.Specs.Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Label}: {s.Value}{(s.Unit is null ? null : " " + s.Unit)}"))]);

        public ProductSummary ToSummary() =>
            new(Sku, Slug, Path, Title, Category, Brand, Image, new Money(Price, Currency), Stock > 0, Rating, ReviewCount, UnitsSold);
    }
}
