using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp.Web;

namespace Api.Content.Umbraco;

/// <summary>
/// Builds ImageSharp.Web resize URLs signed with the same HMAC the CMS validates.
/// </summary>
/// <remarks>
/// Mirrors <c>ImageSharpMiddlewareOptions.OnComputeHMAC</c>'s default (ImageSharp.Web 3.2, used by Umbraco 18): HMAC-SHA256
/// over the lower-cased relative URL (path base + path + the recognised commands in request order), hex encoded,
/// appended as <c>hmac</c>. Unknown query parameters (such as Umbraco's <c>v</c> cache buster) are stripped by the
/// middleware before hashing, so they are passed through but not signed.
/// </remarks>
public sealed class ImageUrlSigner(byte[]? secret)
{
    /// <summary>Returns a relative URL that resizes <paramref name="mediaUrl"/> to <paramref name="width"/> pixels wide.</summary>
    public string Resize(string mediaUrl, int width)
    {
        var queryStart = mediaUrl.IndexOf('?');
        var path = PathString.FromUriComponent(queryStart < 0 ? mediaUrl : mediaUrl[..queryStart]);
        var passthrough = queryStart < 0 ? QueryString.Empty : QueryString.FromUriComponent(mediaUrl[queryStart..]);

        var commands = QueryString.Create([KeyValuePair.Create("width", (string?)width.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        var query = passthrough.Add(commands);

        if (secret is { Length: > 0 })
        {
            var signed = CaseHandlingUriBuilder.BuildRelative(
                CaseHandlingUriBuilder.CaseHandling.LowerInvariant, path: path, query: commands);
            query = query.Add(HMACUtilities.TokenCommand, HMACUtilities.ComputeHMACSHA256(signed, secret));
        }

        return path.ToUriComponent() + query.ToUriComponent();
    }
}
