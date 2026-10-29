using System.Net;
using Api.Commerce;
using Api.Commerce.Drupal;
using Api.Contracts;
using Microsoft.Extensions.Options;

namespace Api.Tests;

/// <summary>Calls made by the Drupal commerce adapter, answered with JSON captured from the headless_commerce module.</summary>
public sealed class DrupalCommerceSourceTests
{
    private readonly StubHandler _handler = new();
    private readonly DrupalCommerceSource _source;

    public DrupalCommerceSourceTests()
    {
        var http = new HttpClient(_handler) { BaseAddress = new Uri("http://drupal/") };
        _source = new DrupalCommerceSource(http, Options.Create(new DrupalOptions { PublicBaseUrl = "https://shop.example.com/" }));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("juniper-nomad-14-dg00001", "DG00001")]
    [InlineData("old-title-dg00001", "DG00001")]
    [InlineData("dg00001", "DG00001")]
    [InlineData("nomad-", null)]
    [InlineData("nomad-dg.1", null)]
    public void Sku_is_the_last_slug_segment(string slug, string? sku) => Assert.Equal(sku, DrupalCommerceSource.SkuFromSlug(slug));

    [Fact]
    public async Task Maps_a_product_with_specs_stats_money_and_public_image()
    {
        _handler.Respond(HttpStatusCode.OK, "drupal/product.json");

        var product = await _source.GetProductAsync("anything-dg00001", Ct);

        Assert.Equal("GET /headless/products/DG00001", Assert.Single(_handler.Requests));
        Assert.NotNull(product);
        Assert.Equal(("DG00001", "juniper-nomad-14-dg00001", "/shop/juniper-nomad-14-dg00001"), (product.Sku, product.Slug, product.Path));
        Assert.Equal(("Laptops", "Juniper"), (product.Category, product.Brand));
        Assert.Equal(new Money(1487.99m, "USD"), product.Price);
        Assert.Equal(53, product.Stock);
        Assert.Equal(new ProductStats(3.7, 148, 1892, 3.2), product.Stats);
        Assert.Equal(10, product.Specs.Length);
        Assert.Equal(new ProductSpec("Memory", "64", "GB"), product.Specs[1]);
        Assert.Equal("https://shop.example.com/sites/default/files/products/laptops-3.jpg", product.Image?.Url);
        Assert.Equal(new DateOnly(2022, 6, 2), product.ReleaseDate);
        Assert.Contains("<h2>Highlights</h2>", product.DescriptionHtml);
        Assert.Contains("<strong>Processor:</strong>", product.DescriptionHtml);
        Assert.StartsWith("The Juniper Nomad 14 is the laptop", product.Summary);
    }

    [Fact]
    public async Task Unknown_product_is_null()
    {
        _handler.Respond(HttpStatusCode.NotFound, json: """{"error":"not_found","message":"No product."}""");

        Assert.Null(await _source.GetProductBySkuAsync("DG99999", Ct));
    }

    [Fact]
    public async Task Empty_cart_has_zero_totals_and_the_token_is_returned_once()
    {
        _handler.Respond(HttpStatusCode.Created, "drupal/cart-empty.json");

        var created = await _source.CreateCartAsync(Ct);

        Assert.Equal("68c9eec7aa4e795cdfdc157bd3571ae00513e37f8f6bdbe2", created.Token);
        Assert.Equal(new Money(0m, "USD"), created.Cart.Total);
        Assert.Empty(created.Cart.Items);
    }

    [Fact]
    public async Task Cart_calls_send_the_token_and_map_lines()
    {
        _handler.Respond(HttpStatusCode.OK, "drupal/cart.json");

        var result = await _source.AddItemAsync("08ed21f5-ec3d-472d-b31e-b973e6cb46f7", "tok", "DG00001", 2, Ct);

        Assert.Equal("POST /headless/carts/08ed21f5-ec3d-472d-b31e-b973e6cb46f7/items", Assert.Single(_handler.Requests));
        Assert.Equal("tok", _handler.CartTokens.Single());
        Assert.Equal("""{"sku":"DG00001","quantity":2}""", _handler.Bodies.Single());
        var line = Assert.Single(result.Value!.Items);
        Assert.Equal(("1", "DG00001", "/shop/juniper-nomad-14-dg00001", 2), (line.Id, line.Sku, line.Path, line.Quantity));
        Assert.Equal(new Money(1487.99m, "USD"), line.UnitPrice);
        Assert.Equal(new Money(2975.98m, "USD"), result.Value.Total);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, CommerceErrorKind.Invalid)]
    [InlineData(HttpStatusCode.PaymentRequired, CommerceErrorKind.PaymentDeclined)]
    [InlineData(HttpStatusCode.NotFound, CommerceErrorKind.NotFound)]
    [InlineData(HttpStatusCode.Conflict, CommerceErrorKind.Conflict)]
    public async Task Refusals_become_typed_errors(HttpStatusCode status, CommerceErrorKind kind)
    {
        _handler.Respond(status, "drupal/checkout-invalid.json");

        var result = await _source.CheckoutAsync("08ed21f5-ec3d-472d-b31e-b973e6cb46f7", "tok",
            new CheckoutRequest("x", new CustomerName("", ""), new PostalAddress("", null, "", "", null, "US"), new PaymentCard("1", 1, 2020, "1")), Ct);

        Assert.Null(result.Value);
        Assert.Equal(kind, result.Error!.Kind);
        Assert.Equal("invalid_checkout", result.Error.Code);
        Assert.Equal("Enter a valid card number.", result.Error.Fields!["number"]);
    }

    [Fact]
    public async Task Server_errors_throw()
    {
        _handler.Respond(HttpStatusCode.InternalServerError, json: "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() => _source.GetCartAsync("08ed21f5-ec3d-472d-b31e-b973e6cb46f7", "tok", Ct));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Json)> _responses = new();

        public List<string> Requests { get; } = [];
        public List<string> CartTokens { get; } = [];
        public List<string> Bodies { get; } = [];

        public void Respond(HttpStatusCode status, string? fixture = null, string? json = null) =>
            _responses.Enqueue((status, json ?? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture!))));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Headers.TryGetValues(DrupalCommerceSource.CartTokenHeader, out var tokens))
            {
                CartTokens.AddRange(tokens);
            }

            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var (status, json) = _responses.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
