using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Api.Auth;
using Api.Commerce;
using Api.Contracts;
using Api.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Api.Tests;

/// <summary>Customer bearer tokens on the order history and checkout endpoints, with customer auth on and off.</summary>
public sealed class CustomerAuthTests
{
    private const string Issuer = "https://idp.example.com/realms/shop";
    private const string CartUri = "/api/carts/08ed21f5-ec3d-472d-b31e-b973e6cb46f7/checkout";
    private static readonly SymmetricSecurityKey Key = new(new byte[32]) { KeyId = "test" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Orders_need_a_valid_customer_token()
    {
        var commerce = new FakeCommerce();
        await using var app = await StartAsync(commerce, authOn: true);
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me/orders", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/me/orders", Token(audience: "other"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/me/orders", Token(expired: true))).StatusCode);

        using var ok = await SendAsync(client, HttpMethod.Get, "/api/me/orders?page=2&pageSize=5", Token());

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal((new CustomerIdentity("customer-1", "ada@example.com"), 2, 5), commerce.OrdersQuery);
    }

    [Fact]
    public async Task Checkout_attaches_the_customer_and_rejects_a_bad_token()
    {
        var commerce = new FakeCommerce();
        await using var app = await StartAsync(commerce, authOn: true);
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, CartUri, Token())).StatusCode);
        Assert.Equal(new CustomerIdentity("customer-1", "ada@example.com"), commerce.CheckoutCustomer);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, CartUri, token: null)).StatusCode);
        Assert.Null(commerce.CheckoutCustomer);

        commerce.Checkouts = 0;
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, CartUri, Token(expired: true))).StatusCode);
        Assert.Equal(0, commerce.Checkouts);
    }

    [Fact]
    public async Task Without_an_authority_everyone_is_a_guest()
    {
        var commerce = new FakeCommerce();
        await using var app = await StartAsync(commerce, authOn: false);
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/me/orders", Token())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, CartUri, Token())).StatusCode);
        Assert.Null(commerce.CheckoutCustomer);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string uri, string? token)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new CheckoutRequest("ada@example.com", new CustomerName("Ada", "Lovelace"),
                new PostalAddress("1 Main St", null, "Springfield", "12345", null, "US"), new PaymentCard("4242424242424242", 12, 2030, "123")));
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Headers.Add(CommerceEndpoints.CartTokenHeader, "tok");
        return await client.SendAsync(request, Ct);
    }

    private static string Token(string audience = "shop-api", bool expired = false)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", "customer-1"), new Claim("email", "ada@example.com")]),
            IssuedAt = now.AddHours(-2),
            NotBefore = now.AddHours(-2),
            Expires = expired ? now.AddHours(-1) : now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });
    }

    private static async Task<WebApplication> StartAsync(FakeCommerce commerce, bool authOn)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Providers:Commerce"] = "Drupal",
            ["CustomerAuth:Authority"] = authOn ? Issuer : null,
            ["CustomerAuth:Audience"] = "shop-api",
        });
        builder.AddCustomerAuth();
        // The keys the authority's discovery document would provide.
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            o.Configuration.SigningKeys.Add(Key);
        });
        builder.Services.AddSingleton<ICommerceSource>(commerce);
        builder.Services.AddSingleton<IProductIndex>(new FakeProductIndex());
        var app = builder.Build();
        app.UseCustomerAuth();
        app.MapCommerceEndpoints(app.Configuration);
        await app.StartAsync(Ct);
        return app;
    }

    private sealed class FakeCommerce : ICommerceSource
    {
        public CustomerIdentity? CheckoutCustomer { get; private set; }
        public int Checkouts { get; set; }
        public (CustomerIdentity, int, int)? OrdersQuery { get; private set; }

        public Task<CommerceResult<OrderReceipt>> CheckoutAsync(
            string cartId, string token, CheckoutRequest request, CustomerIdentity? customer, CancellationToken ct)
        {
            CheckoutCustomer = customer;
            Checkouts++;
            var receipt = new OrderReceipt("1001", DateTimeOffset.UnixEpoch, request.Email, "FAKE", "4242", [], 0, Money.Zero("USD"));
            return Task.FromResult<CommerceResult<OrderReceipt>>(receipt);
        }

        public Task<OrderPage> GetOrdersAsync(CustomerIdentity customer, int page, int pageSize, CancellationToken ct)
        {
            OrdersQuery = (customer, page, pageSize);
            return Task.FromResult(new OrderPage(page, pageSize, 0, []));
        }

        public Task<Product?> GetProductAsync(string slug, CancellationToken ct) => throw new NotSupportedException();

        public Task<Product?> GetProductBySkuAsync(string sku, CancellationToken ct) => throw new NotSupportedException();

        public IAsyncEnumerable<Product> GetAllProductsAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<CreatedCart> CreateCartAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<CommerceResult<Cart>> GetCartAsync(string cartId, string token, CancellationToken ct) => throw new NotSupportedException();

        public Task<CommerceResult<Cart>> AddItemAsync(string cartId, string token, string sku, int quantity, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CommerceResult<Cart>> UpdateItemAsync(string cartId, string token, string itemId, int quantity, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CommerceResult<Cart>> RemoveItemAsync(string cartId, string token, string itemId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProductIndex : IProductIndex
    {
        public Task UpsertAsync(IEnumerable<Product> products, CancellationToken ct) => Task.CompletedTask;

        public Task EnsureIndexAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<ProductSearchResult> SearchAsync(string query, int page, int pageSize, ProductFilters filters, ProductSort sort, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<int> ReplaceAllAsync(IAsyncEnumerable<Product> products, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
