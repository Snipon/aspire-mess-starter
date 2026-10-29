using System.Text.Json.Serialization;

namespace Api.Contracts;

/// <summary>A page section. The <c>type</c> property tells which kind it is.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HeroBlock), "hero")]
[JsonDerivedType(typeof(CtaBlock), "cta")]
[JsonDerivedType(typeof(LatestArticlesBlock), "latestArticles")]
[JsonDerivedType(typeof(ArticleListBlock), "articleList")]
public abstract record Block;

/// <summary>A page header with an optional background image and call to action.</summary>
public sealed record HeroBlock(string Heading, string? Subheading, Image? Image, Link? Cta) : Block;

/// <summary>A call-to-action banner.</summary>
public sealed record CtaBlock(string Heading, string? Text, Link? Link, CtaStyle Style) : Block;

/// <summary>The newest articles (optionally only those with <see cref="Tag"/>), resolved by the API.</summary>
public sealed record LatestArticlesBlock(string? Heading, string? Tag, ArticleSummary[] Articles) : Block;

/// <summary>
/// All articles with topic and year filters and a pager. The client fetches the articles itself (from the search
/// endpoint), because they depend on its query string.
/// </summary>
public sealed record ArticleListBlock(string? Heading, int PageSize) : Block;

[JsonConverter(typeof(JsonStringEnumConverter<CtaStyle>))]
public enum CtaStyle
{
    [JsonStringEnumMemberName("primary")] Primary,
    [JsonStringEnumMemberName("secondary")] Secondary,
}
