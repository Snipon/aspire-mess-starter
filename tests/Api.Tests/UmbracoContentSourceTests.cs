using System.Net;
using Api.Content.Umbraco;

namespace Api.Tests;

/// <summary>Delivery API queries issued by the Umbraco adapter, answered with captured fixtures.</summary>
public sealed class UmbracoContentSourceTests
{
    private readonly FixtureHandler _handler = new();
    private readonly UmbracoContentSource _source;

    public UmbracoContentSourceTests()
    {
        var http = new HttpClient(_handler) { BaseAddress = new Uri("http://cms/") };
        _source = new UmbracoContentSource(http, new DeliveryApiMapper("https://cms.example.com", new ImageUrlSigner(null), [480]));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_articles_newest_first_with_tag_filter_and_paging()
    {
        _handler.Respond("delivery/articles-query-tag.json");

        var result = await _source.GetArticlesAsync("design systems", page: 3, pageSize: 3, culture: null, Ct);

        Assert.Equal(
            "/umbraco/delivery/api/v2/content?filter=contentType:article&filter=tag:design%20systems&sort=publishDate:desc&skip=6&take=3",
            Assert.Single(_handler.Requests));
        Assert.Equal((3, 3, 11), (result.Page, result.PageSize, result.Total));
        Assert.Equal(3, result.Items.Length);
        Assert.All(result.Items, a => Assert.Contains("css", a.Tags));
    }

    [Fact]
    public async Task Page_resolves_latest_articles_through_the_article_query()
    {
        _handler.Respond("delivery/site.json");
        _handler.Respond("delivery/articles-query.json");

        var page = await _source.GetByPathAsync("/", culture: null, Ct);

        Assert.Equal(
            [
                "/umbraco/delivery/api/v2/content/item/?expand=properties[relatedArticle]",
                "/umbraco/delivery/api/v2/content?filter=contentType:article&sort=publishDate:desc&skip=0&take=6",
            ],
            _handler.Requests);
        var latest = Assert.Single(page!.Blocks.OfType<Api.Contracts.LatestArticlesBlock>());
        Assert.Equal(6, latest.Articles.Length);
    }

    [Fact]
    public async Task Counts_tags_over_all_articles()
    {
        _handler.Respond("delivery/articles-query.json");

        var tags = await _source.GetTagsAsync(culture: null, Ct);

        Assert.Equal("/umbraco/delivery/api/v2/content?filter=contentType:article&fields=properties[tags]&sort=sortOrder:asc&skip=0&take=100",
            Assert.Single(_handler.Requests));
        // The 6 fixture articles: dotnet x5, devops x3, typescript x3, aspire x2, design-systems x2, cloud x1, performance x1.
        Assert.Equal(new Api.Contracts.TagCount("dotnet", 5), tags[0]);
        Assert.Equal(["dotnet", "devops", "typescript", "aspire", "design-systems", "cloud", "performance"], tags.Select(t => t.Tag));
    }

    [Fact]
    public async Task Navigation_excludes_articles_and_blog_posts()
    {
        _handler.Respond("delivery/articles-query.json");

        await _source.GetNavigationAsync(culture: null, Ct);

        Assert.Equal("/umbraco/delivery/api/v2/content?filter=contentType:!article&filter=contentType:!blogPost&sort=sortOrder:asc&skip=0&take=100",
            Assert.Single(_handler.Requests));
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();

        public List<string> Requests { get; } = [];

        public void Respond(string fixture) =>
            _responses.Enqueue(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
