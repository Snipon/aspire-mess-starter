using System.Text.Json;

namespace Api.Contracts;

/// <summary>A published content page, independent of the CMS that produced it.</summary>
/// <param name="Summary">Short description (for articles: a plain-text excerpt of the body).</param>
/// <param name="Image">The page's own image (articles); landing pages carry theirs in a <see cref="HeroBlock"/>.</param>
/// <param name="Blocks">Page sections in display order; empty when the content type has none.</param>
/// <param name="Article">Set when <c>contentType</c> is <c>article</c>, otherwise <c>null</c>.</param>
/// <param name="BlogPost">Set when <c>contentType</c> is <c>blogPost</c>, otherwise <c>null</c>.</param>
/// <param name="Properties">CMS properties that are not mapped to a typed field, passed through raw.</param>
public sealed record Page(
    string Id,
    string ContentType,
    string Path,
    string Name,
    string Title,
    string? Summary,
    Image? Image,
    Block[] Blocks,
    Article? Article,
    BlogPost? BlogPost,
    string? Culture,
    DateTimeOffset UpdatedAt,
    Dictionary<string, JsonElement> Properties);
