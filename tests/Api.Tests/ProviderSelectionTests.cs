using Api.Content;
using Api.Content.Umbraco;
using Api.Search;
using Api.Search.Meilisearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Api.Tests;

public sealed class ProviderSelectionTests
{
    private static HostApplicationBuilder CreateBuilder(string? content, string? search) =>
        CreateBuilder(new Dictionary<string, string?>
        {
            ["Providers:Content"] = content,
            ["Providers:Search"] = search,
            ["Umbraco:BaseUrl"] = "http://cms",
            ["Umbraco:PublicBaseUrl"] = "http://localhost:5000",
            ["ConnectionStrings:meilisearch"] = "Endpoint=http://localhost:7700;MasterKey=test",
        });

    private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> config)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(config);
        return builder;
    }

    [Fact]
    public void Selects_configured_adapters()
    {
        var builder = CreateBuilder("Umbraco", "Meilisearch");
        builder.AddContentSource();
        builder.AddSearchIndex();
        using var host = builder.Build();

        Assert.IsType<UmbracoContentSource>(host.Services.GetRequiredService<IContentSource>());
        Assert.IsType<MeilisearchSearchIndex>(host.Services.GetRequiredService<ISearchIndex>());
    }

    [Theory]
    [InlineData("Contentful")]
    [InlineData(null)]
    public void Unknown_content_provider_throws_at_startup(string? provider)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CreateBuilder(provider, "Meilisearch").AddContentSource());
        Assert.Contains("Providers:Content", ex.Message);
    }

    [Theory]
    [InlineData("Algolia")]
    [InlineData(null)]
    public void Unknown_search_provider_throws_at_startup(string? provider)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CreateBuilder("Umbraco", provider).AddSearchIndex());
        Assert.Contains("Providers:Search", ex.Message);
    }
}
