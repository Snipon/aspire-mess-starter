namespace Api.Contracts;

/// <summary>A page of search hits.</summary>
public sealed record SearchResult(string Query, int Page, int PageSize, int Total, SearchHit[] Hits);

/// <summary>
/// A single search hit. <see cref="Highlight"/> is HTML-escaped text in which matches are wrapped in <c>&lt;mark&gt;</c>.
/// </summary>
/// <param name="PublishDate">Set for articles.</param>
/// <param name="ImageUrl">A small image for a thumbnail, if the page has one.</param>
public sealed record SearchHit(
    string Id,
    string Path,
    string Title,
    string? Summary,
    string ContentType,
    string? Highlight,
    string[] Tags,
    DateOnly? PublishDate,
    string? ImageUrl);
