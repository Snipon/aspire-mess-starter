using System.ComponentModel.DataAnnotations;

namespace Api.Content.Umbraco;

public sealed class UmbracoOptions
{
    public const string SectionName = "Umbraco";

    /// <summary>Base address used by the API to call the Delivery API (service discovery, e.g. <c>https+http://cms</c>).</summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Public base URL of the CMS used to build media URLs that browsers load.</summary>
    [Required]
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Delivery API key (sent as the <c>Api-Key</c> header).</summary>
    public string? DeliveryApiKey { get; set; }

    /// <summary>Base64 HMAC secret; must equal the CMS <c>Umbraco:CMS:Imaging:HMACSecretKey</c>.</summary>
    public string? HmacSecretKey { get; set; }

    /// <summary>Widths generated for <c>srcset</c>.</summary>
    public int[] RenditionWidths { get; set; } = [480, 960, 1440, 1920];
}
