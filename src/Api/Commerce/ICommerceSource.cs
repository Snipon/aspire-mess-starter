using Api.Contracts;

namespace Api.Commerce;

/// <summary>
/// Port for the commerce backend: the catalog (live, per product, and in full for indexing), carts and checkout.
/// Implemented by a commerce adapter (see <c>Commerce/Drupal</c>). Cart calls carry the cart's token.
/// </summary>
public interface ICommerceSource
{
    /// <summary>The product a slug points at, also when the slug is outdated (compare with <see cref="Product.Slug"/>).</summary>
    Task<Product?> GetProductAsync(string slug, CancellationToken ct);

    Task<Product?> GetProductBySkuAsync(string sku, CancellationToken ct);

    IAsyncEnumerable<Product> GetAllProductsAsync(CancellationToken ct);

    Task<CreatedCart> CreateCartAsync(CancellationToken ct);

    Task<CommerceResult<Cart>> GetCartAsync(string cartId, string token, CancellationToken ct);

    Task<CommerceResult<Cart>> AddItemAsync(string cartId, string token, string sku, int quantity, CancellationToken ct);

    /// <summary>Sets the quantity; 0 removes the item.</summary>
    Task<CommerceResult<Cart>> UpdateItemAsync(string cartId, string token, string itemId, int quantity, CancellationToken ct);

    Task<CommerceResult<Cart>> RemoveItemAsync(string cartId, string token, string itemId, CancellationToken ct);

    Task<CommerceResult<OrderReceipt>> CheckoutAsync(string cartId, string token, CheckoutRequest request, CancellationToken ct);
}

/// <summary>A value, or the reason the backend refused the operation.</summary>
public sealed record CommerceResult<T>(T? Value, CommerceError? Error)
{
    public static implicit operator CommerceResult<T>(T value) => new(value, null);

    public static implicit operator CommerceResult<T>(CommerceError error) => new(default, error);
}

/// <param name="Code">Machine-readable, e.g. <c>out_of_stock</c>, <c>card_declined</c>, <c>invalid_checkout</c>.</param>
/// <param name="Fields">Field errors of an invalid checkout (email, given, family, line1, locality, postalCode,
/// countryCode, number, expiry, cvc).</param>
/// <param name="Available">Units in stock, for <see cref="CommerceErrorKind.Conflict"/> with code <c>out_of_stock</c>.</param>
public sealed record CommerceError(CommerceErrorKind Kind, string Code, string Message, IReadOnlyDictionary<string, string>? Fields = null, int? Available = null);

public enum CommerceErrorKind
{
    /// <summary>Invalid input (400).</summary>
    Invalid,

    /// <summary>Unknown cart, cart item or product, or a wrong cart token (404).</summary>
    NotFound,

    /// <summary>Out of stock, empty cart, or the cart is already checked out (409).</summary>
    Conflict,

    /// <summary>The card was declined (402).</summary>
    PaymentDeclined,
}
