using System.ComponentModel.DataAnnotations;

namespace Api.Commerce.Drupal;

public sealed class DrupalOptions
{
    public const string SectionName = "Drupal";

    /// <summary>Base address the API calls (the commerce container's endpoint).</summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Public base URL of the Drupal site, for product image URLs that browsers load.</summary>
    [Required]
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Sent as <c>X-Api-Key</c>; must equal the container's <c>COMMERCE_API_KEY</c>.</summary>
    public string? ApiKey { get; set; }
}
