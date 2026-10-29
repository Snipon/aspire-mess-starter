using Api.Contracts;

namespace Api.Commerce.Drupal;

// The JSON of the headless_commerce module's endpoints (src/Commerce/web/modules/custom/headless_commerce).

internal sealed record DrupalProductPage(int Total, int Offset, DrupalProduct[] Items);

internal sealed record DrupalProduct(
    string Sku,
    string Slug,
    string Title,
    string? Description,
    string? Category,
    string? Brand,
    DrupalImage? Image,
    DrupalMoney? Price,
    int Stock,
    DrupalSpec[]? Specs,
    DrupalStats? Stats,
    int? WeightGrams,
    int? WarrantyMonths,
    DateOnly? ReleaseDate,
    DateTimeOffset UpdatedAt);

internal sealed record DrupalImage(string Url, int? Width, int? Height, string? Alt);

/// <summary>Commerce prices are decimal strings with six places (<c>"1487.990000"</c>).</summary>
internal sealed record DrupalMoney(string Amount, string Currency);

internal sealed record DrupalSpec(string Label, string Value, string? Unit);

internal sealed record DrupalStats(double Rating, int ReviewCount, int UnitsSold, double ReturnRate);

internal sealed record DrupalCart(string Id, string? Token, DrupalCartItem[] Items, int ItemCount, DrupalMoney? Subtotal, DrupalMoney? Total);

internal sealed record DrupalCartItem(
    string Id, string? Sku, string? Slug, string Title, DrupalImage? Image, DrupalMoney? UnitPrice, int Quantity, DrupalMoney? Total, int Stock);

internal sealed record DrupalReceipt(
    string OrderNumber,
    DateTimeOffset PlacedAt,
    string Email,
    string PaymentReference,
    string CardLast4,
    DrupalCartItem[] Items,
    int ItemCount,
    DrupalMoney? Total);

/// <summary>Checkout body: the contract's <see cref="CheckoutRequest"/> plus the signed-in customer, if any.</summary>
internal sealed record DrupalCheckout(
    string Email, CustomerName Name, PostalAddress Address, PaymentCard Card, CustomerIdentity? Customer);

internal sealed record DrupalOrderPage(int Total, int Offset, DrupalOrder[] Items);

internal sealed record DrupalOrder(
    string OrderNumber,
    DateTimeOffset PlacedAt,
    string State,
    string Email,
    DrupalCartItem[] Items,
    int ItemCount,
    DrupalMoney? Total);

internal sealed record DrupalError(string Error, string Message, Dictionary<string, string>? Fields, int? Available);
