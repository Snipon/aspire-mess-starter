using System.Net;
using System.Text.RegularExpressions;

namespace Api.Contracts;

/// <summary>Internal indexing shape passed from a content source to a search index.</summary>
/// <param name="ImageUrl">A small rendition of the page's (or its hero's) image, for result thumbnails.</param>
public sealed partial record SearchDocument(
    string Id,
    string Path,
    string Title,
    string? Summary,
    string Body,
    string ContentType,
    string? Culture,
    DateTimeOffset UpdatedAt,
    string[] Tags,
    DateOnly? PublishDate,
    string? ImageUrl)
{
    public static SearchDocument FromPage(Page page)
    {
        var image = page.Image ?? page.Blocks.OfType<HeroBlock>().FirstOrDefault()?.Image;
        return new(
            page.Id,
            page.Path,
            page.Title,
            page.Summary,
            page.Article is { } article ? HtmlToText(article.BodyHtml) : BlocksToText(page.Blocks),
            page.ContentType,
            page.Culture,
            page.UpdatedAt,
            page.Article?.Tags ?? [],
            page.Article?.PublishDate,
            image is null ? null : image.Srcset.FirstOrDefault()?.Url ?? image.Url);
    }

    internal static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = HtmlTag().Replace(html, " ");
        return Whitespace().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    private static string BlocksToText(Block[] blocks) => string.Join(' ', blocks
        .SelectMany<Block, string?>(b => b switch
        {
            HeroBlock hero => [hero.Heading, hero.Subheading],
            CtaBlock cta => [cta.Heading, cta.Text],
            LatestArticlesBlock latest => [latest.Heading],
            _ => [],
        })
        .Where(t => !string.IsNullOrWhiteSpace(t)));

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
