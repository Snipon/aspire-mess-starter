using System.Net;
using System.Text.Json.Serialization;
using Api.Contracts;
using Meilisearch;
using Meilisearch.QueryParameters;
using MeiliIndex = Meilisearch.Index;

namespace Api.Search.Meilisearch;

/// <summary><see cref="ISearchIndex"/> backed by a Meilisearch index named <c>content</c>.</summary>
public sealed class MeilisearchSearchIndex(MeilisearchClient client) : ISearchIndex
{
    public const string IndexName = "content";

    // Control characters as highlight tags so the text can be HTML-encoded before inserting <mark>.
    private const string PreTag = "\u0002";
    private const string PostTag = "\u0003";

    private const string IndexNotFound = "index_not_found";
    private const string DocumentNotFound = "document_not_found";
    private const int BatchSize = 100;

    private MeiliIndex Index => client.Index(IndexName);

    public Task EnsureIndexAsync(CancellationToken ct) => CreateAndConfigureAsync(IndexName, ct);

    private async Task CreateAndConfigureAsync(string uid, CancellationToken ct)
    {
        var create = await client.CreateIndexAsync(uid, "id", ct);
        await client.WaitForTaskAsync(create.TaskUid, cancellationToken: ct); // fails harmlessly if it already exists

        var settings = await client.Index(uid).UpdateSettingsAsync(new Settings
        {
            SearchableAttributes = ["title", "summary", "body"],
            FilterableAttributes = ["contentType", "culture", "ancestorPaths"],
        }, ct);
        await WaitAsync(settings.TaskUid, ct);
    }

    public async Task<SearchResult> SearchAsync(string query, int page, int pageSize, string? contentType, CancellationToken ct)
    {
        ISearchable<Hit> result;
        try
        {
            result = await Index.SearchAsync<Hit>(query, new SearchQuery
            {
                Page = page,
                HitsPerPage = pageSize,
                Filter = contentType is null ? null : $"contentType = \"{contentType}\"",
                AttributesToHighlight = ["title", "summary", "body"],
                AttributesToCrop = ["body"],
                CropLength = 24,
                HighlightPreTag = PreTag,
                HighlightPostTag = PostTag,
            }, ct);
        }
        catch (MeilisearchApiError e) when (e.Code == IndexNotFound)
        {
            // Meilisearch restarted on empty storage; the initializer rebuilds the index shortly.
            return new SearchResult(query, page, pageSize, 0, []);
        }

        var paginated = (PaginatedSearchResult<Hit>)result;
        var hits = paginated.Hits.Select(h => new SearchHit(
            h.Id, h.Path, h.Title, h.Summary, h.ContentType, Highlight(h.Formatted))).ToArray();
        return new SearchResult(query, page, pageSize, paginated.TotalHits, hits);
    }

    public async Task UpsertAsync(IEnumerable<SearchDocument> documents, CancellationToken ct)
    {
        var task = await Index.AddDocumentsAsync(documents.Select(IndexedDocument.From), "id", ct);
        await WaitAsync(task.TaskUid, ct);
    }

    public async Task DeleteAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var task = await Index.DeleteDocumentsAsync(ids, ct);
        await WaitAsync(task.TaskUid, ct);
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        var task = await Index.DeleteAllDocumentsAsync(ct);
        await WaitAsync(task.TaskUid, ct);
    }

    public async Task<int> ReplaceAllAsync(IAsyncEnumerable<SearchDocument> documents, CancellationToken ct)
    {
        // Build into a private staging index and swap it with the live one, so searches never see a partial index
        // and concurrent rebuilds (several api replicas starting at once) cannot interleave.
        var staging = $"{IndexName}_staging_{Guid.NewGuid():N}";
        var count = 0;
        try
        {
            await CreateAndConfigureAsync(staging, ct);
            var batch = new List<SearchDocument>(BatchSize);
            await foreach (var document in documents.WithCancellation(ct))
            {
                batch.Add(document);
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
            // After a swap the staging index holds the previous contents; on failure it holds a partial build.
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

    public async Task<string?> GetPathAsync(string id, CancellationToken ct)
    {
        try
        {
            return (await Index.GetDocumentAsync<PathOnly>(id, ["id", "path"], ct)).Path;
        }
        catch (MeilisearchApiError e) when (e.Code is IndexNotFound or DocumentNotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetIdsBelowPathAsync(string path, CancellationToken ct)
    {
        const int pageSize = 1000;
        var escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var ids = new List<string>();
        try
        {
            for (var offset = 0; ; offset += pageSize)
            {
                var page = await Index.GetDocumentsAsync<PathOnly>(new DocumentsQuery
                {
                    Filter = $"ancestorPaths = \"{escaped}\"",
                    Fields = ["id", "path"],
                    Limit = pageSize,
                    Offset = offset,
                }, ct);
                ids.AddRange(page.Results.Select(d => d.Id));
                if (offset + pageSize >= page.Total)
                {
                    return ids;
                }
            }
        }
        catch (MeilisearchApiError e) when (e.Code == IndexNotFound)
        {
            return [];
        }
    }

    private async Task<int> AddAsync(string uid, List<SearchDocument> batch, CancellationToken ct)
    {
        var task = await client.Index(uid).AddDocumentsAsync(batch.Select(IndexedDocument.From), "id", ct);
        await WaitAsync(task.TaskUid, ct);
        var added = batch.Count;
        batch.Clear();
        return added;
    }

    private async Task WaitAsync(int taskUid, CancellationToken ct)
    {
        var task = await client.WaitForTaskAsync(taskUid, cancellationToken: ct);
        if (task.Status == TaskInfoStatus.Failed)
        {
            throw new InvalidOperationException($"Meilisearch task {taskUid} failed: {task.Error?.GetValueOrDefault("message")}");
        }
    }

    /// <summary>
    /// Prefers a highlighted summary, then a cropped body snippet, then the title; HTML-encoded with matches wrapped in
    /// &lt;mark&gt;.
    /// </summary>
    private static string? Highlight(Formatted? formatted)
    {
        var text = new[] { formatted?.Summary, formatted?.Body, formatted?.Title }.FirstOrDefault(t => t?.Contains(PreTag) == true);
        return text is null
            ? null
            : WebUtility.HtmlEncode(text).Replace(PreTag, "<mark>").Replace(PostTag, "</mark>");
    }

    /// <param name="AncestorPaths">Every path above the document (<c>/a/b/c</c> -> <c>/</c>, <c>/a</c>, <c>/a/b</c>), so the
    /// descendants of a renamed, moved or unpublished page can be found with one filter.</param>
    private sealed record IndexedDocument(
        string Id, string Path, string Title, string? Summary, string Body, string ContentType, string? Culture, long UpdatedAt,
        string[] AncestorPaths)
    {
        public static IndexedDocument From(SearchDocument d) => new(
            d.Id, d.Path, d.Title, d.Summary, d.Body, d.ContentType, d.Culture, d.UpdatedAt.ToUnixTimeSeconds(),
            AncestorsOf(d.Path));
    }

    private sealed record PathOnly(string Id, string Path);

    internal static string[] AncestorsOf(string path)
    {
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var ancestors = new string[segments.Length];
        for (var i = 0; i < ancestors.Length; i++)
        {
            ancestors[i] = "/" + string.Join('/', segments[..i]);
        }

        return ancestors;
    }

    private sealed record Hit(string Id, string Path, string Title, string? Summary, string ContentType)
    {
        [JsonPropertyName("_formatted")]
        public Formatted? Formatted { get; init; }
    }

    private sealed record Formatted(string? Title, string? Summary, string? Body);
}
