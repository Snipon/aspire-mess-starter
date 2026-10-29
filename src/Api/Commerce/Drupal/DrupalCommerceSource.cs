using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Api.Content;
using Api.Contracts;
using Microsoft.Extensions.Options;

namespace Api.Commerce.Drupal;

/// <summary><see cref="ICommerceSource"/> backed by Drupal Commerce through the headless_commerce module's endpoints.</summary>
public sealed class DrupalCommerceSource(HttpClient http, IOptions<DrupalOptions> options) : ICommerceSource
{
    internal const string ProductsApi = "headless/products";
    internal const string CartsApi = "headless/carts";
    internal const string OrdersApi = "headless/orders";
    internal const string CartTokenHeader = "X-Cart-Token";
    private const int PageSize = 200;

    private readonly string _publicBaseUrl = options.Value.PublicBaseUrl.TrimEnd('/');

    public Task<Product?> GetProductAsync(string slug, CancellationToken ct) =>
        SkuFromSlug(slug) is { } sku ? GetProductBySkuAsync(sku, ct) : Task.FromResult<Product?>(null);

    public async Task<Product?> GetProductBySkuAsync(string sku, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{ProductsApi}/{Uri.EscapeDataString(sku)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return ToProduct(await ReadAsync<DrupalProduct>(response, ct));
    }

    public async IAsyncEnumerable<Product> GetAllProductsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        for (var offset = 0; ; offset += PageSize)
        {
            using var response = await http.GetAsync($"{ProductsApi}?offset={offset}&limit={PageSize}", ct);
            var page = await ReadAsync<DrupalProductPage>(response, ct);
            foreach (var product in page.Items)
            {
                yield return ToProduct(product);
            }

            if (page.Items.Length < PageSize || offset + PageSize >= page.Total)
            {
                yield break;
            }
        }
    }

    public async Task<CreatedCart> CreateCartAsync(CancellationToken ct)
    {
        using var response = await http.PostAsync(CartsApi, content: null, ct);
        var cart = await ReadAsync<DrupalCart>(response, ct);
        return new CreatedCart(cart.Token ?? throw new InvalidOperationException("Drupal returned a cart without a token."), ToCart(cart));
    }

    public Task<CommerceResult<Cart>> GetCartAsync(string cartId, string token, CancellationToken ct) =>
        SendAsync<DrupalCart, Cart>(HttpMethod.Get, CartUri(cartId), token, body: null, ToCart, ct);

    public Task<CommerceResult<Cart>> AddItemAsync(string cartId, string token, string sku, int quantity, CancellationToken ct) =>
        SendAsync<DrupalCart, Cart>(HttpMethod.Post, $"{CartUri(cartId)}/items", token, new { sku, quantity }, ToCart, ct);

    public Task<CommerceResult<Cart>> UpdateItemAsync(string cartId, string token, string itemId, int quantity, CancellationToken ct) =>
        SendAsync<DrupalCart, Cart>(HttpMethod.Patch, ItemUri(cartId, itemId), token, new { quantity }, ToCart, ct);

    public Task<CommerceResult<Cart>> RemoveItemAsync(string cartId, string token, string itemId, CancellationToken ct) =>
        SendAsync<DrupalCart, Cart>(HttpMethod.Delete, ItemUri(cartId, itemId), token, body: null, ToCart, ct);

    public Task<CommerceResult<OrderReceipt>> CheckoutAsync(
        string cartId, string token, CheckoutRequest request, CustomerIdentity? customer, CancellationToken ct) =>
        SendAsync<DrupalReceipt, OrderReceipt>(HttpMethod.Post, $"{CartUri(cartId)}/checkout", token,
            new { request.Email, request.Name, request.Address, request.Card, customer }, ToReceipt, ct);

    public async Task<OrderPage> GetOrdersAsync(CustomerIdentity customer, int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;
        using var response = await http.GetAsync(
            $"{OrdersApi}?customer={Uri.EscapeDataString(customer.Subject)}&offset={offset}&limit={pageSize}", ct);
        var orders = await ReadAsync<DrupalOrderPage>(response, ct);
        return new OrderPage(page, pageSize, orders.Total, [.. orders.Items.Select(ToOrder)]);
    }

    /// <summary>"juniper-nomad-14-dg00001" -> "DG00001": slugs end in the lower-cased SKU (see ProductExporter::slug).</summary>
    internal static string? SkuFromSlug(string slug)
    {
        var index = slug.LastIndexOf('-');
        var sku = index < 0 ? slug : slug[(index + 1)..];
        return sku.Length is > 0 and <= 64 && sku.All(char.IsAsciiLetterOrDigit) ? sku.ToUpperInvariant() : null;
    }

    private static string CartUri(string cartId) => $"{CartsApi}/{Uri.EscapeDataString(cartId)}";

    private static string ItemUri(string cartId, string itemId) => $"{CartUri(cartId)}/items/{Uri.EscapeDataString(itemId)}";

    private async Task<CommerceResult<TResult>> SendAsync<TDrupal, TResult>(
        HttpMethod method, string uri, string token, object? body, Func<TDrupal, TResult> map, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(CartTokenHeader, token);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.PaymentRequired or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            var error = await response.Content.ReadFromJsonAsync<DrupalError>(ct);
            var kind = response.StatusCode switch
            {
                HttpStatusCode.BadRequest => CommerceErrorKind.Invalid,
                HttpStatusCode.PaymentRequired => CommerceErrorKind.PaymentDeclined,
                HttpStatusCode.NotFound => CommerceErrorKind.NotFound,
                _ => CommerceErrorKind.Conflict,
            };
            return new CommerceError(kind, error?.Error ?? "error", error?.Message ?? "The commerce backend refused the request.", error?.Fields, error?.Available);
        }

        return map(await ReadAsync<TDrupal>(response, ct));
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct)
            ?? throw new InvalidOperationException($"Drupal returned no {typeof(T).Name}.");
    }

    private Product ToProduct(DrupalProduct p)
    {
        var description = MarkdownText.Parse(p.Description);
        var stats = p.Stats ?? new DrupalStats(0, 0, 0, 0);
        return new Product(
            p.Sku,
            p.Slug,
            ProductPath(p.Slug),
            p.Title,
            MarkdownText.ToHtml(description),
            MarkdownText.Excerpt(description),
            p.Category ?? string.Empty,
            p.Brand ?? string.Empty,
            ToImage(p.Image),
            ToMoney(p.Price),
            p.Stock,
            [.. (p.Specs ?? []).Select(s => new ProductSpec(s.Label, s.Value, s.Unit))],
            new ProductStats(stats.Rating, stats.ReviewCount, stats.UnitsSold, stats.ReturnRate),
            p.WeightGrams,
            p.WarrantyMonths,
            p.ReleaseDate,
            p.UpdatedAt);
    }

    private Cart ToCart(DrupalCart c) =>
        new(c.Id, [.. c.Items.Select(ToCartItem)], c.ItemCount, ToMoney(c.Subtotal), ToMoney(c.Total));

    private OrderReceipt ToReceipt(DrupalReceipt r) =>
        new(r.OrderNumber, r.PlacedAt, r.Email, r.PaymentReference, r.CardLast4, [.. r.Items.Select(ToCartItem)], r.ItemCount, ToMoney(r.Total));

    private Order ToOrder(DrupalOrder o) =>
        new(o.OrderNumber, o.PlacedAt, ToOrderStatus(o.State), o.Email, [.. o.Items.Select(ToCartItem)], o.ItemCount, ToMoney(o.Total));

    // The contract's closed set; Commerce's in-between workflow states (validation, fulfillment, ...) are all "processing".
    internal static string ToOrderStatus(string state) => state switch
    {
        "completed" or "canceled" => state,
        _ => "processing",
    };

    private CartItem ToCartItem(DrupalCartItem i) => new(
        i.Id, i.Sku ?? string.Empty, i.Slug is null ? null : ProductPath(i.Slug), i.Title, ToImage(i.Image), ToMoney(i.UnitPrice), i.Quantity, ToMoney(i.Total), i.Stock);

    internal static string ProductPath(string slug) => $"/shop/{slug}";

    /// <summary>An empty cart has no totals in Commerce; the contract always has them.</summary>
    internal static Money ToMoney(DrupalMoney? money) => money is null
        ? Money.Zero("USD")
        : new Money(Math.Round(decimal.Parse(money.Amount, NumberStyles.Number, CultureInfo.InvariantCulture), 2, MidpointRounding.AwayFromZero), money.Currency);

    // Drupal serves its own files; there are no resized renditions (srcset is empty).
    private Image? ToImage(DrupalImage? image) => image is null
        ? null
        : new Image(_publicBaseUrl + "/" + image.Url.TrimStart('/'), image.Alt, image.Width, image.Height, null, []);
}
