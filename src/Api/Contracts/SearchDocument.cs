using System.Net;
using System.Text.RegularExpressions;

namespace Api.Contracts;

/// <summary>Internal indexing shape passed from a content source to a search index.</summary>
public sealed partial record SearchDocument(
    string Id,
    string Path,
    string Title,
    string? Summary,
    string Body,
    string ContentType,
    string? Culture,
    DateTimeOffset UpdatedAt)
{
    public static SearchDocument FromPage(Page page) => new(
        page.Id,
        page.Path,
        page.Title,
        page.Summary,
        HtmlToText(page.BodyHtml),
        page.ContentType,
        page.Culture,
        page.UpdatedAt);

    internal static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = Tags().Replace(html, " ");
        return Whitespace().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
