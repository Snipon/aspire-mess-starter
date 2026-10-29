using System.Net;
using System.Runtime.CompilerServices;
using Api.Caching;
using Api.Content;
using Api.Contracts;
using Api.Endpoints;
using Api.Search;
using Api.Search.Meilisearch;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

public sealed class SearchSyncTests
{
    [Theory]
    [InlineData("/", true)]
    [InlineData("/about/team", true)]
    [InlineData("/v1.2/notes", true)]
    [InlineData("/../../umbraco/delivery/api/v2/media", false)]
    [InlineData("/about/./team", false)]
    [InlineData("/about/..", false)]
    [InlineData("/a\\b", false)]
    [InlineData("about", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Path_validation_rejects_dot_segments(string? path, bool valid) =>
        Assert.Equal(valid, RequestValidation.IsValidPath(path));

    [Theory]
    [InlineData(null, true)]
    [InlineData("en", true)]
    [InlineData("en-US", true)]
    [InlineData("zh-Hant-TW", true)]
    [InlineData("en US,,", false)]
    [InlineData("bad,,", false)]
    [InlineData("", false)]
    public void Culture_validation(string? culture, bool valid) =>
        Assert.Equal(valid, RequestValidation.IsValidCulture(culture));

    [Theory]
    [InlineData("/", new string[0])]
    [InlineData("/about", new[] { "/" })]
    [InlineData("/about/team/jane", new[] { "/", "/about", "/about/team" })]
    public void Ancestors_of_path(string path, string[] expected) =>
        Assert.Equal(expected, MeilisearchSearchIndex.AncestorsOf(path));

    [Theory]
    [InlineData(null, true)]
    [InlineData("css", true)]
    [InlineData("design-systems", true)]
    [InlineData("Design Systems", true)]
    [InlineData("C#", true)]
    [InlineData(".NET", true)]
    [InlineData("CI/CD", true)]
    [InlineData("a!", true)]
    [InlineData("css,security", false)]
    [InlineData("tab\tbed", false)]
    [InlineData("!css", false)]
    [InlineData("", false)]
    [InlineData(" css", false)]
    public void Tag_validation(string? tag, bool valid) =>
        Assert.Equal(valid, RequestValidation.IsValidTag(tag));

    [Fact]
    public void Search_document_of_an_article_carries_tags_date_and_thumbnail()
    {
        var image = new Image("https://cms/media/a.jpg", null, 1600, 900, null,
            [new ImageRendition(480, "https://cms/media/a.jpg?width=480&hmac=x"), new ImageRendition(960, "https://cms/media/a.jpg?width=960&hmac=y")]);
        var page = new Page("1", "article", "/articles/a", "A", "A", "Excerpt", image, [],
            new Article(new DateOnly(2026, 10, 28), ["css", "security"], "<h2>Intro</h2><p>Body &amp; more</p>", 3),
            null, null, DateTimeOffset.UnixEpoch, []);

        var doc = SearchDocument.FromPage(page);

        Assert.Equal("Intro Body & more", doc.Body);
        Assert.Equal(["css", "security"], doc.Tags);
        Assert.Equal(new DateOnly(2026, 10, 28), doc.PublishDate);
        Assert.Equal("https://cms/media/a.jpg?width=480&hmac=x", doc.ImageUrl);
    }

    [Fact]
    public void Search_document_of_a_landing_page_indexes_block_text()
    {
        var hero = new Image("https://cms/media/h.jpg", null, null, null, null, []);
        var page = new Page("1", "site", "/", "Home", "Home", null, null,
            [
                new HeroBlock("Welcome", "To the site", hero, null),
                new LatestArticlesBlock("Latest", null, []),
                new CtaBlock("Built with Aspire", "Read more", null, CtaStyle.Primary),
            ],
            null, null, null, DateTimeOffset.UnixEpoch, []);

        var doc = SearchDocument.FromPage(page);

        Assert.Equal("Welcome To the site Latest Built with Aspire Read more", doc.Body);
        Assert.Empty(doc.Tags);
        Assert.Null(doc.PublishDate);
        Assert.Equal("https://cms/media/h.jpg", doc.ImageUrl);
    }

    [Theory]
    [InlineData("X-Reindex-Secret", "reindex-secret", HttpStatusCode.OK)]
    [InlineData("X-Reindex-Secret", "webhook-secret", HttpStatusCode.Unauthorized)]
    [InlineData("X-Webhook-Secret", "webhook-secret", HttpStatusCode.Unauthorized)]
    [InlineData("X-Reindex-Secret", "", HttpStatusCode.Unauthorized)]
    public async Task Reindex_requires_its_own_secret(string header, string value, HttpStatusCode expected)
    {
        await using var app = await StartSearchApiAsync(reindexSecret: "reindex-secret");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/search/reindex");
        request.Headers.Add(header, value);

        using var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Reindex_is_refused_when_no_secret_is_configured()
    {
        await using var app = await StartSearchApiAsync(reindexSecret: null);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/search/reindex");
        request.Headers.Add(SharedSecret.ReindexHeader, "");

        using var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Content_webhook_purges_the_web_cache_after_syncing()
    {
        var purges = new List<HttpRequestMessage>();
        await using var app = await StartSearchApiAsync(reindexSecret: null, purges);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/content")
        {
            Content = new StringContent("""{ "id": "1" }""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(SharedSecret.WebhookHeader, "webhook-secret");

        using var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var purge = Assert.Single(purges);
        Assert.Equal("http://web-cache/_purge", purge.RequestUri?.ToString());
        Assert.Equal(["purge-secret"], purge.Headers.GetValues(WebCachePurger.SecretHeader));
    }

    private static async Task<WebApplication> StartSearchApiAsync(string? reindexSecret, List<HttpRequestMessage>? purges = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Webhooks:Secret"] = "webhook-secret",
            ["Search:ReindexSecret"] = reindexSecret,
            ["WebCache:PurgeUrl"] = "http://web-cache/_purge",
            ["WebCache:PurgeSecret"] = "purge-secret",
        });
        var content = new FakeContent();
        content.Add("1", "/");
        builder.Services.AddSingleton<IContentSource>(content);
        builder.Services.AddSingleton<ISearchIndex, FakeIndex>();
        builder.Services.AddTransient<SearchIndexer>();
        builder.Services.AddHttpClient<WebCachePurger>()
            .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(purges ?? []));
        var app = builder.Build();
        app.MapSearchEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    [Fact]
    public async Task Reindex_never_resets_and_replaces_atomically()
    {
        var content = new FakeContent();
        content.Add("1", "/about");
        var index = new FakeIndex();
        var indexer = new SearchIndexer(content, index, NullLogger<SearchIndexer>.Instance);

        Assert.Equal(1, await indexer.ReindexAllAsync(CancellationToken.None));
        Assert.False(index.ResetCalled);
        Assert.Equal("/about", index.Docs["1"].Path);
    }

    [Fact]
    public async Task Reindex_failure_leaves_index_untouched()
    {
        var content = new FakeContent { FailEnumeration = true };
        var index = new FakeIndex();
        index.Docs["1"] = Doc("1", "/about");
        var indexer = new SearchIndexer(content, index, NullLogger<SearchIndexer>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => indexer.ReindexAllAsync(CancellationToken.None));
        Assert.Single(index.Docs);
    }

    [Fact]
    public async Task Renaming_a_parent_resyncs_descendants()
    {
        var content = new FakeContent();
        var index = new FakeIndex();
        index.Docs["p"] = Doc("p", "/about");
        index.Docs["c"] = Doc("c", "/about/team");
        index.Docs["o"] = Doc("o", "/other");
        content.Add("p", "/about-us");
        content.Add("c", "/about-us/team");
        content.Add("o", "/other");

        await new SearchIndexer(content, index, NullLogger<SearchIndexer>.Instance).SyncAsync("p", CancellationToken.None);

        Assert.Equal("/about-us", index.Docs["p"].Path);
        Assert.Equal("/about-us/team", index.Docs["c"].Path);
        Assert.Equal(["p", "c"], content.Fetched);
    }

    [Fact]
    public async Task Unpublishing_a_parent_removes_descendants()
    {
        var content = new FakeContent();
        var index = new FakeIndex();
        index.Docs["p"] = Doc("p", "/about");
        index.Docs["c"] = Doc("c", "/about/team");

        var result = await new SearchIndexer(content, index, NullLogger<SearchIndexer>.Instance).SyncAsync("p", CancellationToken.None);

        Assert.Equal("deleted", result.Action);
        Assert.Empty(index.Docs);
    }

    [Fact]
    public async Task Republishing_without_path_change_does_not_touch_descendants()
    {
        var content = new FakeContent();
        var index = new FakeIndex();
        index.Docs["p"] = Doc("p", "/about");
        index.Docs["c"] = Doc("c", "/about/team");
        content.Add("p", "/about");

        await new SearchIndexer(content, index, NullLogger<SearchIndexer>.Instance).SyncAsync("p", CancellationToken.None);

        Assert.Equal(["p"], content.Fetched);
    }

    private static SearchDocument Doc(string id, string path) =>
        new(id, path, id, null, string.Empty, "page", null, DateTimeOffset.UnixEpoch, [], null, null, null);

    private sealed class FakeContent : IContentSource
    {
        private readonly Dictionary<string, Page> _pages = [];
        public bool FailEnumeration { get; init; }
        public List<string> Fetched { get; } = [];

        public void Add(string id, string path) =>
            _pages[id] = new Page(id, "page", path, id, id, null, null, [], null, null, null, DateTimeOffset.UnixEpoch, []);

        public Task<Page?> GetByIdAsync(string id, string? culture, CancellationToken ct)
        {
            Fetched.Add(id);
            return Task.FromResult(_pages.GetValueOrDefault(id));
        }

        public Task<Page?> GetByPathAsync(string path, string? culture, CancellationToken ct) => throw new NotSupportedException();

        public Task<NavItem[]> GetNavigationAsync(string? culture, CancellationToken ct) => throw new NotSupportedException();

        public Task<Paged<ArticleSummary>> GetArticlesAsync(string? tag, int page, int pageSize, string? culture, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<TagCount[]> GetTagsAsync(string? culture, CancellationToken ct) => throw new NotSupportedException();

        public async IAsyncEnumerable<SearchDocument> GetAllForIndexingAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            if (FailEnumeration)
            {
                throw new HttpRequestException("cms down");
            }

            foreach (var page in _pages.Values)
            {
                yield return SearchDocument.FromPage(page);
            }
        }
    }

    private sealed class FakeIndex : ISearchIndex
    {
        public Dictionary<string, SearchDocument> Docs { get; } = [];
        public bool ResetCalled { get; private set; }

        public Task EnsureIndexAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<SearchResult> SearchAsync(string query, int page, int pageSize, SearchFilters filters, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task UpsertAsync(IEnumerable<SearchDocument> documents, CancellationToken ct)
        {
            foreach (var d in documents) Docs[d.Id] = d;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IEnumerable<string> ids, CancellationToken ct)
        {
            foreach (var id in ids) Docs.Remove(id);
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken ct)
        {
            ResetCalled = true;
            Docs.Clear();
            return Task.CompletedTask;
        }

        public async Task<int> ReplaceAllAsync(IAsyncEnumerable<SearchDocument> documents, CancellationToken ct)
        {
            var staged = new List<SearchDocument>();
            await foreach (var d in documents.WithCancellation(ct)) staged.Add(d);
            if (staged.Count == 0) return 0;
            Docs.Clear();
            foreach (var d in staged) Docs[d.Id] = d;
            return staged.Count;
        }

        public Task<long> CountAsync(CancellationToken ct) => Task.FromResult((long)Docs.Count);

        public Task<string?> GetPathAsync(string id, CancellationToken ct) => Task.FromResult(Docs.GetValueOrDefault(id)?.Path);

        public Task<IReadOnlyList<string>> GetIdsBelowPathAsync(string path, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Docs.Values
                .Where(d => MeilisearchSearchIndex.AncestorsOf(d.Path).Contains(path)).Select(d => d.Id).ToList());
    }

    private sealed class RecordingHandler(List<HttpRequestMessage> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
