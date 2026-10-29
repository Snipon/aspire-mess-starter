namespace Api.Contracts;

/// <summary>An amount of money; <see cref="Amount"/> has two decimals.</summary>
public sealed record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);
}

/// <summary>One specification row, e.g. <c>Memory: 32 GB</c>.</summary>
public sealed record ProductSpec(string Label, string Value, string? Unit);

/// <summary>Performance statistics of a product.</summary>
/// <param name="Rating">Average customer rating, 0–5.</param>
/// <param name="ReturnRate">Share of units returned, in percent.</param>
public sealed record ProductStats(double Rating, int ReviewCount, int UnitsSold, double ReturnRate);

/// <summary>A product on its own page. <see cref="Path"/> is the site path (<c>/shop/{slug}</c>).</summary>
/// <param name="Slug">Canonical slug; a request for an outdated slug still finds the product (by SKU) and should
/// redirect here.</param>
/// <param name="DescriptionHtml">Rendered from Markdown like article bodies (raw HTML escaped, unsafe links neutralised).</param>
/// <param name="Summary">Plain-text excerpt of the description (about 160 characters).</param>
public sealed record Product(
    string Sku,
    string Slug,
    string Path,
    string Title,
    string DescriptionHtml,
    string? Summary,
    string Category,
    string Brand,
    Image? Image,
    Money Price,
    int Stock,
    ProductSpec[] Specs,
    ProductStats Stats,
    int? WeightGrams,
    int? WarrantyMonths,
    DateOnly? ReleaseDate,
    DateTimeOffset UpdatedAt);

/// <summary>A product in a listing.</summary>
public sealed record ProductSummary(
    string Sku,
    string Slug,
    string Path,
    string Title,
    string Category,
    string Brand,
    Image? Image,
    Money Price,
    bool InStock,
    double Rating,
    int ReviewCount,
    int UnitsSold)
{
    public static ProductSummary From(Product p) => new(
        p.Sku, p.Slug, p.Path, p.Title, p.Category, p.Brand, p.Image, p.Price, p.Stock > 0, p.Stats.Rating, p.Stats.ReviewCount,
        p.Stats.UnitsSold);
}

/// <summary>A page of products with facet counts (each counted without its own filter, like <see cref="SearchFacets"/>).</summary>
public sealed record ProductSearchResult(string Query, string Sort, int Page, int PageSize, int Total, ProductSummary[] Items, ProductFacets Facets);

/// <param name="Ratings">Counts per whole star (<c>4</c> = rated 4.0–4.9), highest first; filter with <c>minRating</c>.</param>
/// <param name="Availability"><c>inStock</c> and <c>outOfStock</c>.</param>
public sealed record ProductFacets(FacetCount[] Categories, FacetCount[] Brands, FacetCount[] Ratings, FacetCount[] Availability)
{
    public static readonly ProductFacets Empty = new([], [], [], []);
}

/// <summary>A shopping cart. Its id and token (returned once, on creation) are needed for every later call.</summary>
public sealed record Cart(string Id, CartItem[] Items, int ItemCount, Money Subtotal, Money Total);

/// <param name="Stock">Units currently available, so the quantity picker can be limited.</param>
public sealed record CartItem(string Id, string Sku, string? Path, string Title, Image? Image, Money UnitPrice, int Quantity, Money Total, int Stock);

/// <summary>A new cart and the token that authorises it (send it as <c>X-Cart-Token</c>; it is not shown again).</summary>
public sealed record CreatedCart(string Token, Cart Cart);

public sealed record AddCartItem(string Sku, int Quantity = 1);

/// <param name="Quantity">0 removes the item.</param>
public sealed record UpdateCartItem(int Quantity);

/// <summary>Checkout details. The card is checked by the fake processor (see README) and never stored.</summary>
public sealed record CheckoutRequest(string Email, CustomerName Name, PostalAddress Address, PaymentCard Card);

public sealed record CustomerName(string Given, string Family);

/// <param name="CountryCode">ISO 3166-1 alpha-2, e.g. <c>US</c>.</param>
public sealed record PostalAddress(string Line1, string? Line2, string Locality, string PostalCode, string? AdministrativeArea, string CountryCode);

public sealed record PaymentCard(string Number, int ExpMonth, int ExpYear, string Cvc);

/// <summary>A placed order.</summary>
public sealed record OrderReceipt(
    string OrderNumber,
    DateTimeOffset PlacedAt,
    string Email,
    string PaymentReference,
    string CardLast4,
    CartItem[] Items,
    int ItemCount,
    Money Total);

/// <summary>A placed order in the customer's order history.</summary>
/// <param name="Status"><c>processing</c>, <c>completed</c> or <c>canceled</c>.</param>
public sealed record Order(
    string OrderNumber,
    DateTimeOffset PlacedAt,
    string Status,
    string Email,
    CartItem[] Items,
    int ItemCount,
    Money Total);

/// <summary>A page of the customer's orders, newest first.</summary>
public sealed record OrderPage(int Page, int PageSize, int Total, Order[] Items);
