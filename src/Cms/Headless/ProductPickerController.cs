using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;

namespace Cms.Headless;

public sealed class ProductPickerOptions
{
    public const string SectionName = "ProductPicker";

    /// <summary>Base URL of the API, whose <c>/api/products</c> search the product picker uses.</summary>
    public string? ApiUrl { get; set; }
}

/// <summary>
/// Product search for the product picker property editor (<c>wwwroot/App_Plugins/ProductPicker</c>), for signed-in
/// backoffice users. Passes through the API's product search, so the CMS knows the API contract, not the commerce
/// backend. 503 when no API is configured or the shop is off.
/// </summary>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("product-picker")]
[ApiExplorerSettings(GroupName = "Product picker")]
public sealed class ProductPickerController(IHttpClientFactory httpClientFactory, IOptions<ProductPickerOptions> options)
    : ManagementApiControllerBase
{
    private const int MaxQueryLength = 100;
    private const int PageSize = 10;

    /// <summary>Returns the API's <c>ProductSearchResult</c> for <paramref name="q"/> (by title, brand, SKU, ...).</summary>
    [HttpGet("products")]
    public async Task<IActionResult> Search(string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiUrl))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var query = (q ?? string.Empty).Trim();
        query = query[..Math.Min(query.Length, MaxQueryLength)];
        var url = $"{options.Value.ApiUrl.TrimEnd('/')}/api/products?q={Uri.EscapeDataString(query)}&pageSize={PageSize}";
        using HttpClient client = httpClientFactory.CreateClient(nameof(ProductPickerController));
        client.Timeout = TimeSpan.FromSeconds(10);
        using HttpResponseMessage response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Content(await response.Content.ReadAsStringAsync(ct), "application/json");
    }
}
