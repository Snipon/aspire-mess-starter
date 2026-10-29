using System.Text.RegularExpressions;
using Api.Auth;
using Api.Commerce;
using Api.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Api.Endpoints;

public static partial class CommerceEndpoints
{
    public const string CartTokenHeader = "X-Cart-Token";
    public const int MaxQuantity = 99;

    private static readonly Dictionary<string, ProductSort> Sorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["relevance"] = ProductSort.Relevance,
        ["popular"] = ProductSort.Popular,
        ["price-asc"] = ProductSort.PriceAsc,
        ["price-desc"] = ProductSort.PriceDesc,
        ["rating"] = ProductSort.Rating,
        ["newest"] = ProductSort.Newest,
    };

    /// <summary>Maps the shop endpoints unless the shop is off (<c>Providers:Commerce</c> empty or <c>None</c>).</summary>
    public static IEndpointRouteBuilder MapCommerceEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        if (CommerceServiceCollectionExtensions.IsOff(configuration["Providers:Commerce"]))
        {
            return app;
        }

        var products = app.MapGroup("/api/products").WithTags("Commerce");
        products.MapGet("/", SearchProducts)
            .WithName("SearchProducts")
            .WithSummary("Search and list products")
            .WithDescription("Full-text search over title, brand, category, SKU and specs. `category`, `brand`, `minRating` (whole " +
                "stars) and `inStock` narrow the results; `facets` counts each, ignoring that facet's own filter. `sort`: " +
                "relevance (default; popular when `q` is empty), popular, price-asc, price-desc, rating, newest. Stock and sales " +
                "come from the index (refreshed every few minutes and after each order); the product page has live values.");
        products.MapGet("/{slug}", GetProduct)
            .WithName("GetProduct")
            .WithSummary("Get a product by slug")
            .WithDescription("Live from the commerce backend. An outdated slug still finds the product (it ends in the SKU); " +
                "redirect when `slug` differs from the requested one.");

        var carts = app.MapGroup("/api/carts").WithTags("Commerce");
        carts.MapPost("/", CreateCart)
            .WithName("CreateCart")
            .WithSummary("Create an empty cart")
            .WithDescription($"Returns the cart and its token. Every other cart call needs the token in `{CartTokenHeader}`; a " +
                "missing or wrong token answers 404, like an unknown cart.");
        carts.MapGet("/{cartId}", GetCart).WithName("GetCart").WithSummary("Get a cart");
        carts.MapPost("/{cartId}/items", AddItem)
            .WithName("AddCartItem")
            .WithSummary("Add a product to the cart")
            .WithDescription($"Adds `quantity` (1–{MaxQuantity}) of the product with `sku`, merging with an existing line. 409 " +
                "`out_of_stock` (with `available`) when the cart would hold more than is in stock.");
        carts.MapPatch("/{cartId}/items/{itemId}", UpdateItem)
            .WithName("UpdateCartItem")
            .WithSummary("Change the quantity of a cart line")
            .WithDescription($"`quantity` 0–{MaxQuantity}; 0 removes the line.");
        carts.MapDelete("/{cartId}/items/{itemId}", RemoveItem).WithName("RemoveCartItem").WithSummary("Remove a cart line");
        carts.MapPost("/{cartId}/checkout", Checkout)
            .WithName("Checkout")
            .WithSummary("Pay with the fake card processor and place the order")
            .WithDescription("Demo payment, no money moves. Card 4242 4242 4242 4242 (or any other valid number) is approved; " +
                "4000 0000 0000 0002 is declined (`card_declined`), 4000 0000 0000 9995 `insufficient_funds`, " +
                "4000 0000 0000 0069 `expired_card` (402). 400 `invalid_checkout` lists field errors in `fields`; 409 for an " +
                "empty cart, missing stock or a cart that was already checked out. With a customer's bearer token (see " +
                "`GetMyOrders`) the order belongs to that customer; an invalid or expired token answers 401.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/me/orders", GetMyOrders)
            .WithTags("Customer")
            .WithName("GetMyOrders")
            .WithSummary("The signed-in customer's orders")
            .WithDescription("Needs the customer's access token from the identity provider (`Authorization: Bearer`, audience " +
                "`CustomerAuth:Audience`); 401 without a valid one, and always when customer sign-in is off. Newest first.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<ProductSearchResult>, BadRequest<ProblemDetails>>> SearchProducts(
        IProductIndex index, string? q, string? category, string? brand, int? minRating, bool? inStock, string? sort,
        CancellationToken ct, int page = 1, int pageSize = 24)
    {
        if (!RequestValidation.IsValidPaging(page, pageSize))
        {
            return TypedResults.BadRequest(Problems.BadRequest(RequestValidation.PagingError));
        }

        if (q?.Length > 200 || !IsValidValue(category) || !IsValidValue(brand))
        {
            return TypedResults.BadRequest(Problems.BadRequest("'q' must be at most 200 characters; 'category' and 'brand' at most 100, without control characters."));
        }

        if (minRating is < 1 or > 5)
        {
            return TypedResults.BadRequest(Problems.BadRequest("'minRating' must be between 1 and 5."));
        }

        var order = ProductSort.Relevance;
        if (sort is not null && !Sorts.TryGetValue(sort, out order))
        {
            return TypedResults.BadRequest(Problems.BadRequest($"'sort' must be one of: {string.Join(", ", Sorts.Keys)}."));
        }

        var filters = new ProductFilters(category, brand, minRating, inStock);
        return TypedResults.Ok(await index.SearchAsync(q?.Trim() ?? string.Empty, page, pageSize, filters, order, ct));
    }

    private static async Task<Results<Ok<Product>, NotFound<ProblemDetails>>> GetProduct(ICommerceSource commerce, string slug, CancellationToken ct)
    {
        var product = Slug().IsMatch(slug) ? await commerce.GetProductAsync(slug, ct) : null;
        return product is null ? TypedResults.NotFound(Problems.NotFound($"No product at '{slug}'.")) : TypedResults.Ok(product);
    }

    private static async Task<Created<CreatedCart>> CreateCart(ICommerceSource commerce, CancellationToken ct)
    {
        var cart = await commerce.CreateCartAsync(ct);
        return TypedResults.Created($"/api/carts/{cart.Cart.Id}", cart);
    }

    private static async Task<Results<Ok<Cart>, ProblemHttpResult>> GetCart(
        ICommerceSource commerce, string cartId, [FromHeader(Name = CartTokenHeader)] string? token, CancellationToken ct) =>
        ToResult(IsValidCart(cartId, token) ? await commerce.GetCartAsync(cartId, token!, ct) : (CommerceResult<Cart>)CartNotFound);

    private static async Task<Results<Ok<Cart>, ProblemHttpResult>> AddItem(
        ICommerceSource commerce, string cartId, [FromHeader(Name = CartTokenHeader)] string? token, AddCartItem item, CancellationToken ct)
    {
        if (!IsValidCart(cartId, token))
        {
            return ToResult<Cart>(CartNotFound);
        }

        if (string.IsNullOrWhiteSpace(item.Sku) || item.Sku.Length > 64 || item.Quantity is < 1 or > MaxQuantity)
        {
            return Invalid($"'sku' is required and 'quantity' must be between 1 and {MaxQuantity}.");
        }

        return ToResult(await commerce.AddItemAsync(cartId, token!, item.Sku, item.Quantity, ct));
    }

    private static async Task<Results<Ok<Cart>, ProblemHttpResult>> UpdateItem(
        ICommerceSource commerce, string cartId, string itemId, [FromHeader(Name = CartTokenHeader)] string? token, UpdateCartItem item,
        CancellationToken ct)
    {
        if (!IsValidCart(cartId, token) || !IsValidItem(itemId))
        {
            return ToResult<Cart>(CartNotFound);
        }

        if (item.Quantity is < 0 or > MaxQuantity)
        {
            return Invalid($"'quantity' must be between 0 and {MaxQuantity}.");
        }

        return ToResult(await commerce.UpdateItemAsync(cartId, token!, itemId, item.Quantity, ct));
    }

    private static async Task<Results<Ok<Cart>, ProblemHttpResult>> RemoveItem(
        ICommerceSource commerce, string cartId, string itemId, [FromHeader(Name = CartTokenHeader)] string? token, CancellationToken ct) =>
        ToResult(IsValidCart(cartId, token) && IsValidItem(itemId) ? await commerce.RemoveItemAsync(cartId, token!, itemId, ct) : (CommerceResult<Cart>)CartNotFound);

    private static async Task<Results<Ok<OrderReceipt>, ProblemHttpResult>> Checkout(
        ICommerceSource commerce, IProductIndex index, ILoggerFactory loggers, HttpContext http, string cartId,
        [FromHeader(Name = CartTokenHeader)] string? token, CheckoutRequest request, CancellationToken ct)
    {
        if (!IsValidCart(cartId, token))
        {
            return ToResult<OrderReceipt>(CartNotFound);
        }

        // A signed-in customer whose token was rejected must not silently become a guest (the order would be missing
        // from their history): the caller refreshes the token and tries again.
        if (http.HasRejectedToken())
        {
            return TypedResults.Problem("The customer's access token is invalid or expired.", statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_token" });
        }

        var result = await commerce.CheckoutAsync(cartId, token!, request, http.GetCustomer(), ct);
        if (result.Value is { } receipt)
        {
            // Stock and units sold changed: refresh those products in the listing now instead of at the next reindex.
            try
            {
                var sold = await Task.WhenAll(receipt.Items.Select(i => commerce.GetProductBySkuAsync(i.Sku, ct)));
                await index.UpsertAsync(sold.OfType<Product>(), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                loggers.CreateLogger(typeof(CommerceEndpoints)).LogWarning(ex, "Could not re-index the products of order {Order}", receipt.OrderNumber);
            }
        }

        return ToResult(result);
    }

    private static async Task<Results<Ok<OrderPage>, BadRequest<ProblemDetails>, UnauthorizedHttpResult>> GetMyOrders(
        ICommerceSource commerce, HttpContext http, CancellationToken ct, int page = 1, int pageSize = 10)
    {
        if (http.GetCustomer() is not { } customer)
        {
            return TypedResults.Unauthorized();
        }

        if (!RequestValidation.IsValidPaging(page, pageSize))
        {
            return TypedResults.BadRequest(Problems.BadRequest(RequestValidation.PagingError));
        }

        return TypedResults.Ok(await commerce.GetOrdersAsync(customer, page, pageSize, ct));
    }

    private static readonly CommerceError CartNotFound = new(CommerceErrorKind.NotFound, "cart_not_found", "No such cart.");

    private static Results<Ok<T>, ProblemHttpResult> ToResult<T>(CommerceResult<T> result)
    {
        if (result.Value is { } value)
        {
            return TypedResults.Ok(value);
        }

        var error = result.Error!;
        var status = error.Kind switch
        {
            CommerceErrorKind.Invalid => StatusCodes.Status400BadRequest,
            CommerceErrorKind.NotFound => StatusCodes.Status404NotFound,
            CommerceErrorKind.PaymentDeclined => StatusCodes.Status402PaymentRequired,
            _ => StatusCodes.Status409Conflict,
        };
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error.Fields is { Count: > 0 })
        {
            extensions["fields"] = error.Fields;
        }

        if (error.Available is not null)
        {
            extensions["available"] = error.Available;
        }

        return TypedResults.Problem(error.Message, statusCode: status, extensions: extensions);
    }

    private static ProblemHttpResult Invalid(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = "invalid_request" });

    private static bool IsValidCart(string cartId, string? token) =>
        Guid.TryParse(cartId, out _) && token is { Length: > 0 and <= 128 } && token.All(char.IsAsciiLetterOrDigit);

    private static bool IsValidItem(string itemId) => itemId.Length is > 0 and <= 20 && itemId.All(char.IsAsciiDigit);

    private static bool IsValidValue(string? value) => value is null || (value.Length is > 0 and <= 100 && !value.Any(char.IsControl));

    [GeneratedRegex("^[a-z0-9-]{1,200}$")]
    private static partial Regex Slug();
}
