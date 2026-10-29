using System.Net;
using Api.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

public sealed class WebCachePurgerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WebCachePurger Purger(Func<HttpRequestMessage, HttpResponseMessage> respond, string? url = "http://cache/_purge", string? secret = "s3cret") =>
        new(new HttpClient(new Handler(respond)),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebCache:PurgeUrl"] = url,
                ["WebCache:PurgeSecret"] = secret,
            }).Build(),
            NullLogger<WebCachePurger>.Instance);

    [Fact]
    public async Task Posts_to_the_purge_url_with_the_secret()
    {
        HttpRequestMessage? sent = null;

        await Purger(request => { sent = request; return new HttpResponseMessage(HttpStatusCode.NoContent); }).PurgeAsync(Ct);

        Assert.Equal(HttpMethod.Post, sent?.Method);
        Assert.Equal("http://cache/_purge", sent?.RequestUri?.ToString());
        Assert.Equal(["s3cret"], sent!.Headers.GetValues(WebCachePurger.SecretHeader));
    }

    [Theory]
    [InlineData(null, "s3cret")]
    [InlineData("http://cache/_purge", null)]
    public async Task Does_nothing_without_url_or_secret(string? url, string? secret)
    {
        var calls = 0;

        await Purger(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.NoContent); }, url, secret).PurgeAsync(Ct);

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Failures_are_swallowed_so_the_webhook_still_succeeds()
    {
        await Purger(_ => throw new HttpRequestException("down")).PurgeAsync(Ct);
        await Purger(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)).PurgeAsync(Ct);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
