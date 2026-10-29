namespace Api.Contracts;

/// <summary>Article-specific fields of a <see cref="Page"/>.</summary>
/// <param name="BodyHtml">The body rendered from Markdown. Raw HTML in the source is escaped and unsafe link schemes
/// are neutralised, so it can be inserted as-is.</param>
/// <param name="ReadingTimeMinutes">Estimated at 200 words per minute; at least 1.</param>
/// <param name="ProductSku">SKU of the shop product the article refers to, as set in the CMS.</param>
/// <param name="Product">That product from the shop; <c>null</c> when none is set, it is no longer in the shop, or the
/// shop is off or unreachable.</param>
public sealed record Article(
    DateOnly PublishDate,
    string[] Tags,
    string BodyHtml,
    int ReadingTimeMinutes,
    string? ProductSku = null,
    ProductSummary? Product = null);

/// <summary>An article in a list.</summary>
/// <param name="Summary">Plain-text excerpt of the body (about 160 characters).</param>
public sealed record ArticleSummary(
    string Id,
    string Path,
    string Title,
    string? Summary,
    Image? Image,
    DateOnly PublishDate,
    string[] Tags);

/// <summary>Blog-post-specific fields of a <see cref="Page"/>.</summary>
/// <param name="BodyHtml">Rendered and sanitised like <see cref="Article.BodyHtml"/>.</param>
/// <param name="RelatedArticle">The article the post refers to; <c>null</c> when none is set or it is not published.</param>
public sealed record BlogPost(DateOnly PublishDate, string BodyHtml, int ReadingTimeMinutes, ArticleSummary? RelatedArticle);

/// <summary>A blog post in a list.</summary>
/// <param name="Summary">Plain-text excerpt of the body (about 160 characters).</param>
public sealed record BlogPostSummary(string Id, string Path, string Title, string? Summary, Image? Image, DateOnly PublishDate);

/// <summary>A tag and the number of published articles that carry it.</summary>
public sealed record TagCount(string Tag, int Count);

/// <summary>A page of items; <see cref="Total"/> counts all items across pages.</summary>
public sealed record Paged<T>(int Page, int PageSize, int Total, T[] Items);
