namespace Api.Contracts;

/// <summary>Every published page and product with its last change, for the website's sitemap.</summary>
/// <param name="Products">Empty when the shop is off.</param>
public sealed record Sitemap(SitemapPage[] Pages, SitemapProduct[] Products);

public sealed record SitemapPage(string Path, DateTimeOffset UpdatedAt);

public sealed record SitemapProduct(string Slug, DateTimeOffset UpdatedAt);
