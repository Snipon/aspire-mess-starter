using System.Net;
using System.Text.Json;
using Api.Content;
using Api.Contracts;
using Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Api.Tests;

/// <summary>Draft pages for the website's staff preview: shared key, never cached.</summary>
public sealed class PreviewEndpointsTests
{
    private const string Key = "k3y-for-tests";
    private const string DraftId = "11111111-1111-1111-1111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    [InlineData("k3y-for-tests-but-longer")]
    public async Task Rejects_a_missing_or_wrong_key(string? key)
    {
        var content = new DraftContent();
        await using var app = await StartAsync(content, Key);

        using var response = await GetAsync(app, $"/api/preview/content/{DraftId}", key);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Empty(content.Fetched);
    }

    [Fact]
    public async Task Returns_the_draft_with_a_valid_key()
    {
        await using var app = await StartAsync(new DraftContent(), Key);

        using var response = await GetAsync(app, $"/api/preview/content/{DraftId}", Key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("Draft title", json.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("22222222-2222-2222-2222-222222222222")]
    [InlineData("not-a-guid")]
    public async Task Unknown_or_malformed_id_is_not_found(string id)
    {
        await using var app = await StartAsync(new DraftContent(), Key);

        using var response = await GetAsync(app, $"/api/preview/content/{id}", Key);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Invalid_culture_is_a_bad_request()
    {
        await using var app = await StartAsync(new DraftContent(), Key);

        using var response = await GetAsync(app, $"/api/preview/content/{DraftId}?culture=%25%25%25", Key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Is_off_without_a_configured_key(string? header)
    {
        await using var app = await StartAsync(new DraftContent(), configuredKey: null);

        using var response = await GetAsync(app, $"/api/preview/content/{DraftId}", header);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    private static async Task<WebApplication> StartAsync(IContentSource content, string? configuredKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Preview:ApiKey"] = configuredKey });
        builder.Services.AddSingleton(content);
        var app = builder.Build();
        app.MapPreviewEndpoints();
        await app.StartAsync(Ct);
        return app;
    }

    private static Task<HttpResponseMessage> GetAsync(WebApplication app, string url, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (key is not null)
        {
            request.Headers.Add(SharedSecret.PreviewHeader, key);
        }

        return app.GetTestClient().SendAsync(request, Ct);
    }

    private sealed class DraftContent : IContentSource
    {
        public List<string> Fetched { get; } = [];

        public Task<Page?> GetPreviewByIdAsync(string id, string? culture, CancellationToken ct)
        {
            Fetched.Add(id);
            return Task.FromResult(id == DraftId
                ? new Page(id, "page", "/draft", "Draft", "Draft title", null, null, [], null, null, culture, DateTimeOffset.UnixEpoch, [])
                : null);
        }

        public Task<Page?> GetByPathAsync(string path, string? culture, CancellationToken ct) => throw new NotSupportedException();

        public Task<Page?> GetByIdAsync(string id, string? culture, CancellationToken ct) => throw new NotSupportedException();

        public Task<NavItem[]> GetNavigationAsync(string? culture, CancellationToken ct) => throw new NotSupportedException();

        public Task<Paged<ArticleSummary>> GetArticlesAsync(string? tag, int page, int pageSize, string? culture, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<TagCount[]> GetTagsAsync(string? culture, CancellationToken ct) => throw new NotSupportedException();

        public IAsyncEnumerable<SearchDocument> GetAllForIndexingAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
