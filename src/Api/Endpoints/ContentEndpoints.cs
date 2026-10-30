using Api.Commerce;
using Api.Content;
using Api.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Api.Endpoints;

public static class ContentEndpoints
{
    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Content");

        group.MapGet("/content", GetByPath)
            .WithName("GetContentByPath")
            .WithSummary("Get a page by its URL path")
            .WithDescription("Returns the published page at `path` (e.g. `/articles`; `/` is the home page).");

        group.MapGet("/content/{id}", GetById)
            .WithName("GetContentById")
            .WithSummary("Get a page by id");

        group.MapGet("/navigation", GetNavigation)
            .WithName("GetNavigation")
            .WithSummary("Get the navigation tree")
            .WithDescription("Root pages (normally the home page) with their descendants as children, in CMS sort order. " +
                "Articles are not included; list them with `/api/articles`.");

        group.MapGet("/articles", GetArticles)
            .WithName("GetArticles")
            .WithSummary("List published articles, newest first")
            .WithDescription("Ordered by publish date, descending. `tag` keeps only articles with that tag (case-insensitive).");

        group.MapGet("/tags", GetTags)
            .WithName("GetTags")
            .WithSummary("List article tags with counts")
            .WithDescription("Every tag used by a published article and how many articles carry it, most used first.");

        return app;
    }

    private static async Task<Results<Ok<Page>, NotFound<ProblemDetails>, BadRequest<ProblemDetails>>> GetByPath(
        IContentSource content, IServiceProvider services, string? path, string? culture, CancellationToken ct)
    {
        if (!RequestValidation.IsValidPath(path))
        {
            return TypedResults.BadRequest(Problems.BadRequest(
                "Query parameter 'path' is required, must start with '/' and may not contain '.' or '..' segments."));
        }

        if (!RequestValidation.IsValidCulture(culture))
        {
            return TypedResults.BadRequest(Problems.BadRequest(RequestValidation.CultureError));
        }

        var page = await content.GetByPathAsync(path, culture, ct);
        return page is null
            ? TypedResults.NotFound(Problems.NotFound($"No page at path '{path}'."))
            : TypedResults.Ok(await WithProductAsync(page, services, ct));
    }

    private static async Task<Results<Ok<Page>, NotFound<ProblemDetails>, BadRequest<ProblemDetails>>> GetById(
        IContentSource content, IServiceProvider services, string id, string? culture, CancellationToken ct)
    {
        if (!RequestValidation.IsValidCulture(culture))
        {
            return TypedResults.BadRequest(Problems.BadRequest(RequestValidation.CultureError));
        }

        var page = await content.GetByIdAsync(id, culture, ct);
        return page is null
            ? TypedResults.NotFound(Problems.NotFound($"No page with id '{id}'."))
            : TypedResults.Ok(await WithProductAsync(page, services, ct));
    }

    /// <summary>
    /// Fills <see cref="Article.Product"/> live from the shop. The article is returned without it when the shop is off
    /// (no <see cref="ICommerceSource"/>), the product is gone or the commerce backend fails.
    /// </summary>
    internal static async Task<Page> WithProductAsync(Page page, IServiceProvider services, CancellationToken ct)
    {
        if (page.Article is not { ProductSku: { Length: > 0 } sku } article || services.GetService<ICommerceSource>() is not { } commerce)
        {
            return page;
        }

        try
        {
            return await commerce.GetProductBySkuAsync(sku, ct) is { } product
                ? page with { Article = article with { Product = ProductSummary.From(product) } }
                : page;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ContentEndpoints))
                .LogWarning(ex, "Could not load product {Sku} for page {PageId}", sku, page.Id);
            return page;
        }
    }

    private static async Task<Results<Ok<Paged<ArticleSummary>>, BadRequest<ProblemDetails>>> GetArticles(
        IContentSource content, string? tag, string? culture, CancellationToken ct, int page = 1, int pageSize = 10)
    {
        if (!RequestValidation.IsValidPaging(page, pageSize))
        {
            return TypedResults.BadRequest(Problems.BadRequest(RequestValidation.PagingError));
        }

        if (!RequestValidation.IsValidTag(tag))
        {
            return TypedResults.BadRequest(Problems.BadRequest(RequestValidation.TagError));
        }

        if (!RequestValidation.IsValidCulture(culture))
        {
            return TypedResults.BadRequest(Problems.BadRequest(RequestValidation.CultureError));
        }

        return TypedResults.Ok(await content.GetArticlesAsync(tag, page, pageSize, culture, ct));
    }

    private static async Task<Results<Ok<TagCount[]>, BadRequest<ProblemDetails>>> GetTags(
        IContentSource content, string? culture, CancellationToken ct) =>
        RequestValidation.IsValidCulture(culture)
            ? TypedResults.Ok(await content.GetTagsAsync(culture, ct))
            : TypedResults.BadRequest(Problems.BadRequest(RequestValidation.CultureError));

    private static async Task<Results<Ok<NavItem[]>, BadRequest<ProblemDetails>>> GetNavigation(
        IContentSource content, string? culture, CancellationToken ct) =>
        RequestValidation.IsValidCulture(culture)
            ? TypedResults.Ok(await content.GetNavigationAsync(culture, ct))
            : TypedResults.BadRequest(Problems.BadRequest(RequestValidation.CultureError));
}
