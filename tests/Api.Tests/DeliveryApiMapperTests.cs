using System.Text.Json;
using Api.Content.Umbraco;

namespace Api.Tests;

public sealed class DeliveryApiMapperTests
{
    private static readonly byte[] Secret = new byte[64];

    private static readonly DeliveryApiMapper Mapper =
        new("https://cms.example.com/", new ImageUrlSigner(Secret), [480, 960, 1440, 1920]);

    [Fact]
    public void Maps_delivery_api_item_to_page()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "delivery-api-page.json")));

        var page = Mapper.ToPage(json.RootElement, culture: "en-US");

        Assert.Equal("8d0f2b1e-6a3c-4b9a-9e47-0f0f6a1d2c33", page.Id);
        Assert.Equal("page", page.ContentType);
        Assert.Equal("/about-us", page.Path);
        Assert.Equal("About us", page.Name);
        Assert.Equal("About Us", page.Title);
        Assert.Equal("Who we are and what we do.", page.Summary);
        Assert.Equal("en-US", page.Culture);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 30, 0, TimeSpan.Zero), page.UpdatedAt);
        Assert.Equal(
            "<p>Hello <strong>world</strong></p><p><img src=\"https://cms.example.com/media/xyz789/inline.png\" alt=\"\"></p>",
            page.BodyHtml);

        // Only unmapped properties are passed through raw.
        Assert.Equal(["showInFooter"], page.Properties.Keys);
        Assert.True(page.Properties["showInFooter"].GetBoolean());

        var image = Assert.IsType<Api.Contracts.Image>(page.Image);
        Assert.Equal("https://cms.example.com/media/abc123/Team Photo.jpg", image.Url);
        Assert.Equal("Team photo", image.Alt);
        Assert.Equal((1500, 1000), (image.Width, image.Height));
        Assert.Equal(new Api.Contracts.FocalPoint(0.3, 0.6), image.FocalPoint);

        // Renditions wider than the original are skipped; each is absolute and signed.
        Assert.Equal([480, 960, 1440], image.Srcset.Select(r => r.Width));
        Assert.All(image.Srcset, r =>
        {
            Assert.StartsWith($"https://cms.example.com/media/abc123/Team%20Photo.jpg?width={r.Width}&hmac=", r.Url);
            Assert.Equal(new ImageUrlSigner(Secret).Resize("/media/abc123/Team Photo.jpg", r.Width), r.Url["https://cms.example.com".Length..]);
        });
    }

    [Fact]
    public void Falls_back_to_name_when_title_missing_and_tolerates_missing_properties()
    {
        using var json = JsonDocument.Parse("""
            { "id": "1", "contentType": "home", "name": "Home", "updateDate": "2026-01-01T00:00:00Z",
              "route": { "path": "/" }, "properties": { "image": [] } }
            """);

        var page = Mapper.ToPage(json.RootElement, culture: null);

        Assert.Equal("/", page.Path);
        Assert.Equal("Home", page.Title);
        Assert.Null(page.Summary);
        Assert.Null(page.BodyHtml);
        Assert.Null(page.Image);
        Assert.Empty(page.Properties);
    }

    [Theory]
    [InlineData("/about/", "/about")]
    [InlineData("about", "/about")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void Normalizes_paths(string? input, string expected) =>
        Assert.Equal(expected, DeliveryApiMapper.NormalizePath(input));
}
