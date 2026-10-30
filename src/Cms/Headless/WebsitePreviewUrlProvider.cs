using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;

namespace Cms.Headless;

/// <summary>
/// "Preview on website" in the Save and preview menu (App_Plugins/WebsitePreview): opens the draft on the website's
/// staff preview, <c>{Preview:WebUrl}/preview/{key}</c>. Adds no published URLs; offers nothing when no URL is set.
/// </summary>
internal sealed class WebsitePreviewUrlProvider(IConfiguration configuration) : IUrlProvider
{
    public string Alias => "website";

    public UrlInfo? GetUrl(IPublishedContent content, UrlMode mode, string? culture, Uri current) => null;

    public IEnumerable<UrlInfo> GetOtherUrls(int id, Uri current) => [];

    public Task<UrlInfo?> GetPreviewUrlAsync(IContent content, string? culture, string? segment)
    {
        var webUrl = configuration["Preview:WebUrl"];
        if (string.IsNullOrWhiteSpace(webUrl))
        {
            return Task.FromResult<UrlInfo?>(null);
        }

        var query = string.IsNullOrEmpty(culture) ? string.Empty : $"?culture={Uri.EscapeDataString(culture)}";
        return Task.FromResult<UrlInfo?>(UrlInfo.AsUrl($"{webUrl.TrimEnd('/')}/preview/{content.Key:D}{query}", Alias, culture, isExternal: true));
    }
}
