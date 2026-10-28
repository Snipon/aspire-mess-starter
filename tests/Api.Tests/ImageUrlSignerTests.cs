using System.Net;
using System.Security.Cryptography;
using Api.Content.Umbraco;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Web;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Middleware;

namespace Api.Tests;

/// <summary>
/// Validates signed URLs with the real ImageSharp.Web 3.2 middleware (the validator Umbraco 18 runs) configured with
/// the same HMAC secret, and against <see cref="RequestAuthorizationUtilities"/> (what Umbraco's URL generator calls).
/// </summary>
public sealed class ImageUrlSignerTests : IAsyncLifetime
{
    private const string MediaPath = "/media/AbC123/Team Photo.png";
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(64);

    private readonly string _webRoot = Directory.CreateTempSubdirectory("api-tests-").FullName;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var mediaDir = Path.Combine(_webRoot, "media", "AbC123");
        Directory.CreateDirectory(mediaDir);
        using (var image = new Image<Rgba32>(1200, 800, Color.CornflowerBlue))
        {
            await image.SaveAsPngAsync(Path.Combine(mediaDir, "Team Photo.png"));
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            WebRootPath = _webRoot,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddImageSharp(o => o.HMACSecretKey = Secret)
            .Configure<SixLabors.ImageSharp.Web.Caching.PhysicalFileSystemCacheOptions>(o => o.CacheRootPath = Path.Combine(_webRoot, "..", Path.GetFileName(_webRoot) + "-cache"));
        _app = builder.Build();
        _app.UseImageSharp();
        _app.UseStaticFiles();
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Theory]
    [InlineData(480)]
    [InlineData(960)]
    public async Task Signed_resize_url_is_accepted_by_ImageSharp_middleware(int width)
    {
        var url = new ImageUrlSigner(Secret).Resize(MediaPath, width);

        using var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var image = Image.Load(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal(width, image.Width);
    }

    [Fact]
    public async Task Signed_url_with_cache_buster_is_accepted()
    {
        var url = new ImageUrlSigner(Secret).Resize(MediaPath + "?v=1dbd0ac6a1a4f50", 480);

        using var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.StartsWith("/media/AbC123/Team%20Photo.png?v=1dbd0ac6a1a4f50&width=480&hmac=", url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unsigned_and_tampered_urls_are_rejected()
    {
        var signed = new ImageUrlSigner(Secret).Resize(MediaPath, 480);
        var wrongKey = new ImageUrlSigner(RandomNumberGenerator.GetBytes(64)).Resize(MediaPath, 480);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(new ImageUrlSigner(null).Resize(MediaPath, 480), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(signed.Replace("width=480", "width=481"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(wrongKey, ct)).StatusCode);
    }

    [Fact]
    public void Token_matches_RequestAuthorizationUtilities_used_by_Umbraco_url_generator()
    {
        var url = new ImageUrlSigner(Secret).Resize(MediaPath, 1440);
        var token = url[(url.IndexOf("&hmac=", StringComparison.Ordinal) + "&hmac=".Length)..];
        var utilities = _app.Services.GetRequiredService<RequestAuthorizationUtilities>();

        // Umbraco's ImageSharpImageUrlGenerator: ComputeHMAC(QueryHelpers.AddQueryString(imageUrl, commands), Sanitize).
        var expected = utilities.ComputeHMAC($"{MediaPath.Replace(" ", "%20")}?width=1440", CommandHandling.Sanitize);

        Assert.Equal(expected, token);
    }
}
