using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Contracts;
using Api.Search;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Api.Endpoints;

public static partial class SearchEndpoints
{
    private const int MaxPageSize = 50;

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var search = app.MapGroup("/api/search").WithTags("Search");

        search.MapGet("/", Search)
            .WithName("Search")
            .WithSummary("Full-text search")
            .WithDescription("An empty `q` returns no hits. `highlight` is HTML-escaped text with matches wrapped in `<mark>`.");

        search.MapPost("/reindex", Reindex)
            .WithName("Reindex")
            .WithSummary("Rebuild the search index from the content source")
            .WithDescription($"Builds a fresh index and swaps it in atomically; search keeps serving the previous contents " +
                $"until the rebuild has succeeded. Requires the `{WebhookSecret.HeaderName}` header.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPost("/api/webhooks/content", ContentWebhook)
            .WithTags("Webhooks")
            .WithName("ContentWebhook")
            .WithSummary("Content changed webhook")
            .WithDescription(
                $"Called by the CMS on publish/unpublish/delete/move with `{{ \"id\": \"...\" }}` (or an array of those, as sent " +
                $"for move and moved-to-recycle-bin). Each item is re-read from the content source and upserted if published, " +
                $"otherwise removed; when its path changed or it disappeared, the documents indexed below its old path are " +
                $"re-read as well. " +
                $"Requires the `{WebhookSecret.HeaderName}` header.")
            .Accepts<ContentWebhookPayload>("application/json")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<SearchResult>, BadRequest<ProblemDetails>>> Search(
        ISearchIndex index, string? q, string? contentType, CancellationToken ct, int page = 1, int pageSize = 10)
    {
        if (page < 1 || pageSize is < 1 or > MaxPageSize)
        {
            return TypedResults.BadRequest(Problems.BadRequest($"'page' must be >= 1 and 'pageSize' between 1 and {MaxPageSize}."));
        }

        if (contentType is not null && !ContentTypeAlias().IsMatch(contentType))
        {
            return TypedResults.BadRequest(Problems.BadRequest("'contentType' may only contain letters, digits, '-' and '_'."));
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return TypedResults.Ok(new SearchResult(q ?? string.Empty, page, pageSize, 0, []));
        }

        return TypedResults.Ok(await index.SearchAsync(q.Trim(), page, pageSize, contentType, ct));
    }

    private static async Task<Results<Ok<ReindexResult>, UnauthorizedHttpResult>> Reindex(
        [FromHeader(Name = WebhookSecret.HeaderName)] string? secret, IConfiguration configuration, SearchIndexer indexer, CancellationToken ct)
    {
        if (!WebhookSecret.IsValid(secret, configuration))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new ReindexResult(await indexer.ReindexAllAsync(ct)));
    }

    private static async Task<Results<Ok<ContentSyncResult[]>, UnauthorizedHttpResult, BadRequest<ProblemDetails>>> ContentWebhook(
        [FromHeader(Name = WebhookSecret.HeaderName)] string? secret, IConfiguration configuration, SearchIndexer indexer, HttpRequest request,
        CancellationToken ct)
    {
        if (!WebhookSecret.IsValid(secret, configuration))
        {
            return TypedResults.Unauthorized();
        }

        // Umbraco sends a single object for publish/unpublish/delete and an array for moved-to-recycle-bin.
        List<string> ids;
        try
        {
            using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            ids = ExtractIds(body.RootElement);
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest(Problems.BadRequest("Body must be JSON."));
        }

        if (ids.Count == 0)
        {
            return TypedResults.BadRequest(Problems.BadRequest("Payload must contain 'id'."));
        }

        var results = new List<ContentSyncResult>(ids.Count);
        foreach (var id in ids)
        {
            results.Add(await indexer.SyncAsync(id, ct));
        }

        return TypedResults.Ok(results.ToArray());
    }

    private static List<string> ExtractIds(JsonElement root)
    {
        IEnumerable<JsonElement> items = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray(),
            JsonValueKind.Object => [root],
            _ => [],
        };

        var ids = new List<string>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in item.EnumerateObject())
            {
                if (string.Equals(prop.Name, "id", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                {
                    ids.Add(prop.Value.GetString()!);
                }
            }
        }

        return ids;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ContentTypeAlias();
}
