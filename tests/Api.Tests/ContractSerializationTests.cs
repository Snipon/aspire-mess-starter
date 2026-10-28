using System.Text.Json;
using Api.Contracts;

namespace Api.Tests;

/// <summary>The wire shape of polymorphic blocks, with the serializer defaults minimal APIs use.</summary>
public sealed class ContractSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly Block[] Blocks =
    [
        new HeroBlock("Hi", null, null, new Link("/articles", "All", null)),
        new CtaBlock("Go", "Text", null, CtaStyle.Secondary),
        new LatestArticlesBlock("Latest", "css",
            [new ArticleSummary("1", "/articles/a", "A", "S", null, new DateOnly(2026, 10, 28), ["css"])]),
    ];

    [Fact]
    public void Blocks_are_written_with_a_type_discriminator()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Blocks, Web));
        var items = json.RootElement.EnumerateArray().ToArray();

        Assert.Equal(["hero", "cta", "latestArticles"], items.Select(i => i.GetProperty("type").GetString()));
        Assert.Equal("type", items[0].EnumerateObject().First().Name);
        Assert.Equal("/articles", items[0].GetProperty("cta").GetProperty("url").GetString());
        Assert.Equal("secondary", items[1].GetProperty("style").GetString());
        var article = items[2].GetProperty("articles")[0];
        Assert.Equal("2026-10-28", article.GetProperty("publishDate").GetString());
    }

    [Fact]
    public void Blocks_round_trip()
    {
        var back = JsonSerializer.Deserialize<Block[]>(JsonSerializer.Serialize(Blocks, Web), Web)!;

        Assert.IsType<HeroBlock>(back[0]);
        Assert.Equal(CtaStyle.Secondary, Assert.IsType<CtaBlock>(back[1]).Style);
        Assert.Equal("css", Assert.IsType<LatestArticlesBlock>(back[2]).Articles[0].Tags[0]);
    }
}
