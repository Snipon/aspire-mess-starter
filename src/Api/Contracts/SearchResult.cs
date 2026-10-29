namespace Api.Contracts;

/// <summary>A page of search hits, with facet counts for narrowing them.</summary>
public sealed record SearchResult(string Query, int Page, int PageSize, int Total, SearchHit[] Hits, SearchFacets Facets);

/// <summary>
/// Facet counts, most frequent first (years newest first). Each facet is counted with every filter applied except its
/// own, so a value's count is the number of results selecting it (instead of the current value) gives.
/// </summary>
public sealed record SearchFacets(FacetCount[] ContentTypes, FacetCount[] Tags, FacetCount[] Years)
{
    public static readonly SearchFacets Empty = new([], [], []);
}

public sealed record FacetCount(string Value, int Count);

/// <summary>
/// A single search hit. <see cref="Highlight"/> is HTML-escaped text in which matches are wrapped in <c>&lt;mark&gt;</c>.
/// </summary>
/// <param name="PublishDate">Set for articles.</param>
/// <param name="ImageUrl">A small image for a thumbnail, if the page has one.</param>
/// <param name="Image">The same image with all renditions, for cards.</param>
public sealed record SearchHit(
    string Id,
    string Path,
    string Title,
    string? Summary,
    string ContentType,
    string? Highlight,
    string[] Tags,
    DateOnly? PublishDate,
    string? ImageUrl,
    Image? Image);
