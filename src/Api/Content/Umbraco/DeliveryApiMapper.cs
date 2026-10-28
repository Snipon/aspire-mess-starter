using System.Text.Json;
using Api.Contracts;

namespace Api.Content.Umbraco;

/// <summary>Maps Umbraco Delivery API v2 content JSON to contract types.</summary>
public sealed class DeliveryApiMapper(string publicBaseUrl, ImageUrlSigner signer, int[] renditionWidths)
{
    private static readonly HashSet<string> MappedProperties = ["title", "summary", "body", "image"];

    private readonly string _publicBaseUrl = publicBaseUrl.TrimEnd('/');

    public Page ToPage(JsonElement item, string? culture)
    {
        var name = item.GetProperty("name").GetString() ?? string.Empty;
        var properties = item.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object
            ? props
            : default;

        var extra = new Dictionary<string, JsonElement>();
        if (properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (!MappedProperties.Contains(property.Name))
                {
                    extra[property.Name] = property.Value.Clone();
                }
            }
        }

        var title = GetString(properties, "title");
        return new Page(
            Id: item.GetProperty("id").GetString()!,
            ContentType: item.GetProperty("contentType").GetString()!,
            Path: GetPath(item),
            Name: name,
            Title: string.IsNullOrWhiteSpace(title) ? name : title,
            Summary: GetString(properties, "summary"),
            BodyHtml: GetRichText(properties, "body"),
            Image: GetImage(properties, "image"),
            Culture: culture,
            UpdatedAt: item.TryGetProperty("updateDate", out var updated) ? updated.GetDateTimeOffset() : default,
            Properties: extra);
    }

    public static string GetPath(JsonElement item)
    {
        var path = item.TryGetProperty("route", out var route) && route.TryGetProperty("path", out var p)
            ? p.GetString()
            : null;
        return NormalizePath(path);
    }

    /// <summary>Normalises to a leading slash and no trailing slash (root is <c>/</c>).</summary>
    public static string NormalizePath(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim().Trim('/');
        return "/" + trimmed;
    }

    public string ToAbsoluteUrl(string url) => IsHttpUrl(url) ? url : _publicBaseUrl + "/" + url.TrimStart('/');

    // Not Uri.TryCreate(..., Absolute): on Linux "/media/x.jpg" parses as an absolute file:// URI.
    private static bool IsHttpUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement properties, string name) =>
        properties.ValueKind == JsonValueKind.Object
        && properties.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private string? GetRichText(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(name, out var value))
        {
            return null;
        }

        // Rich text is { "markup": "...", "blocks": [] } unless RichTextOutputAsJson is enabled.
        var markup = value.ValueKind switch
        {
            JsonValueKind.Object when value.TryGetProperty("markup", out var m) => m.GetString(),
            JsonValueKind.String => value.GetString(),
            _ => null,
        };

        // Make embedded media absolute so the browser loads it from the CMS's public URL.
        return markup?.Replace("src=\"/media/", $"src=\"{_publicBaseUrl}/media/", StringComparison.Ordinal);
    }

    private Image? GetImage(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(name, out var value))
        {
            return null;
        }

        // Media pickers return an array of media items (or a single item, depending on configuration).
        var media = value.ValueKind switch
        {
            JsonValueKind.Array when value.GetArrayLength() > 0 => value[0],
            JsonValueKind.Object => value,
            _ => default,
        };
        if (media.ValueKind != JsonValueKind.Object
            || !media.TryGetProperty("url", out var urlElement)
            || urlElement.GetString() is not { Length: > 0 } url)
        {
            return null;
        }

        var width = GetInt(media, "width");
        var height = GetInt(media, "height");

        FocalPoint? focalPoint = null;
        if (media.TryGetProperty("focalPoint", out var fp) && fp.ValueKind == JsonValueKind.Object)
        {
            focalPoint = new FocalPoint(fp.GetProperty("left").GetDouble(), fp.GetProperty("top").GetDouble());
        }

        var alt = media.TryGetProperty("properties", out var mediaProps)
            ? GetString(mediaProps, "altText") ?? GetString(mediaProps, "alt")
            : null;
        alt ??= media.TryGetProperty("name", out var mediaName) ? mediaName.GetString() : null;

        // Only CMS-hosted (relative) media can be resized by the CMS's ImageSharp middleware.
        var srcset = !IsHttpUrl(url)
            ? renditionWidths
                .Where(w => width is null || w <= width)
                .Select(w => new ImageRendition(w, ToAbsoluteUrl(signer.Resize(url, w))))
                .ToArray()
            : [];

        return new Image(ToAbsoluteUrl(url), alt, width, height, focalPoint, srcset);
    }

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
