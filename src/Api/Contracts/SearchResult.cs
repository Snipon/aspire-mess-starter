namespace Api.Contracts;

/// <summary>A page of search hits.</summary>
public sealed record SearchResult(string Query, int Page, int PageSize, int Total, SearchHit[] Hits);

/// <summary>
/// A single search hit. <see cref="Highlight"/> is HTML-escaped text in which matches are wrapped in <c>&lt;mark&gt;</c>.
/// </summary>
public sealed record SearchHit(string Id, string Path, string Title, string? Summary, string ContentType, string? Highlight);
