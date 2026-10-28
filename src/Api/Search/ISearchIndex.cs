using Api.Contracts;

namespace Api.Search;

/// <summary>Port for a full-text search index. Implemented by a search adapter (see <c>Search/Meilisearch</c>).</summary>
public interface ISearchIndex
{
    Task EnsureIndexAsync(CancellationToken ct);

    Task<SearchResult> SearchAsync(string query, int page, int pageSize, string? contentType, CancellationToken ct);

    Task UpsertAsync(IEnumerable<SearchDocument> documents, CancellationToken ct);

    Task DeleteAsync(IEnumerable<string> ids, CancellationToken ct);

    /// <summary>Removes all documents from the index.</summary>
    Task ResetAsync(CancellationToken ct);

    /// <summary>
    /// Replaces the whole index with <paramref name="documents"/> atomically: searches keep hitting the previous
    /// contents until the enumeration has completed, and the live index is left untouched if the enumeration (or
    /// indexing) fails. An empty enumeration also leaves the index unchanged (treated as "source not ready").
    /// Returns the number of documents indexed.
    /// </summary>
    Task<int> ReplaceAllAsync(IAsyncEnumerable<SearchDocument> documents, CancellationToken ct);

    /// <summary>Number of documents in the index; 0 when the index does not exist.</summary>
    Task<long> CountAsync(CancellationToken ct);

    /// <summary>The indexed path of the document, or <c>null</c> when it is not indexed.</summary>
    Task<string?> GetPathAsync(string id, CancellationToken ct);

    /// <summary>Ids of all indexed documents whose path is strictly below <paramref name="path"/>.</summary>
    Task<IReadOnlyList<string>> GetIdsBelowPathAsync(string path, CancellationToken ct);
}
