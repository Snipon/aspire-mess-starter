using Api.Content;
using Api.Contracts;

namespace Api.Search;

/// <summary>Moves content from the <see cref="IContentSource"/> into the <see cref="ISearchIndex"/>.</summary>
public sealed class SearchIndexer(IContentSource content, ISearchIndex index, ILogger<SearchIndexer> logger)
{
    /// <summary>
    /// Rebuilds the index from the content source without ever exposing an empty or partial index: the adapter swaps
    /// in the new contents only after the source enumeration has completed successfully.
    /// </summary>
    public async Task<int> ReindexAllAsync(CancellationToken ct)
    {
        await index.EnsureIndexAsync(ct);
        var count = await index.ReplaceAllAsync(content.GetAllForIndexingAsync(ct), ct);
        logger.LogInformation("Reindexed {Count} documents", count);
        return count;
    }

    /// <summary>
    /// Upserts the item if it is currently published, otherwise removes it from the index. When the item disappeared
    /// or its path changed (rename, move, unpublish), the documents indexed below its previous path are re-read too,
    /// because the CMS only notifies about the item itself.
    /// </summary>
    public async Task<ContentSyncResult> SyncAsync(string id, CancellationToken ct)
    {
        var previousPath = await index.GetPathAsync(id, ct);
        var result = await SyncOneAsync(id, ct);

        if (previousPath is not null && (result.Page is null || result.Page.Path != previousPath))
        {
            var descendants = await index.GetIdsBelowPathAsync(previousPath, ct);
            foreach (var descendantId in descendants)
            {
                await SyncOneAsync(descendantId, ct);
            }

            if (descendants.Count > 0)
            {
                logger.LogInformation("Re-synced {Count} descendants of {Id} (was {Path})", descendants.Count, id, previousPath);
            }
        }

        return result.Result;
    }

    private async Task<(Page? Page, ContentSyncResult Result)> SyncOneAsync(string id, CancellationToken ct)
    {
        var page = await content.GetByIdAsync(id, culture: null, ct);
        if (page is null)
        {
            await index.DeleteAsync([id], ct);
            return (null, new ContentSyncResult(id, "deleted"));
        }

        await index.UpsertAsync([SearchDocument.FromPage(page)], ct);
        return (page, new ContentSyncResult(id, "upserted"));
    }
}
