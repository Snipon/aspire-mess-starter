namespace Api.Contracts;

/// <summary>Article-specific fields of a <see cref="Page"/>.</summary>
/// <param name="BodyHtml">The body rendered from Markdown. Raw HTML in the source is escaped and unsafe link schemes
/// are neutralised, so it can be inserted as-is.</param>
/// <param name="ReadingTimeMinutes">Estimated at 200 words per minute; at least 1.</param>
public sealed record Article(DateOnly PublishDate, string[] Tags, string BodyHtml, int ReadingTimeMinutes);

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

/// <summary>A tag and the number of published articles that carry it.</summary>
public sealed record TagCount(string Tag, int Count);

/// <summary>A page of items; <see cref="Total"/> counts all items across pages.</summary>
public sealed record Paged<T>(int Page, int PageSize, int Total, T[] Items);
