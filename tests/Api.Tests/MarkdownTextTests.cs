using Api.Content;

namespace Api.Tests;

public sealed class MarkdownTextTests
{
    private static string Html(string markdown, Func<string, string>? rewrite = null) =>
        MarkdownText.ToHtml(MarkdownText.Parse(markdown), rewrite);

    [Theory]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("Hi <img src=x onerror=alert(1)> there", "&lt;img src=x onerror=alert(1)&gt;")]
    [InlineData("<div onclick=\"alert(1)\">\n\nblock\n\n</div>", "&lt;div onclick=&quot;alert(1)&quot;&gt;")]
    public void Raw_html_is_escaped(string markdown, string expected)
    {
        var html = Html(markdown);

        Assert.Contains(expected, html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<div", html);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JavaScript:alert(1))")]
    [InlineData("[x](javascript&#58;alert(1))")]
    [InlineData("[x](<java\tscript:alert(1)>)")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("[x](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("![x](vbscript:msgbox)")]
    public void Unsafe_link_schemes_are_neutralised(string markdown)
    {
        var html = Html(markdown);

        // The link text may still read "javascript:...", but no URL attribute keeps the scheme.
        Assert.DoesNotMatch("(?i)(href|src)=\"[^\"]*(script|data):", html);
        Assert.Matches("(href|src)=\"#\"", html);
    }

    [Fact]
    public void Generic_attributes_are_not_enabled()
    {
        var html = Html("# Title {onclick=alert(1)}\n\n[x](https://example.com){onmouseover=alert(1)}");

        Assert.DoesNotContain(" onclick=", html);
        Assert.DoesNotContain(" onmouseover=", html);
    }

    [Fact]
    public void Safe_links_are_kept_and_rewritten()
    {
        var html = Html(
            "[a](https://example.com/x?y=1) [b](/articles/b) [c](#top) [d](mailto:a@example.com) ![i](/media/abc/i.jpg) www.example.org",
            url => url.StartsWith("/media/") ? "https://cms" + url : url);

        Assert.Contains("href=\"https://example.com/x?y=1\"", html);
        Assert.Contains("href=\"/articles/b\"", html);
        Assert.Contains("href=\"#top\"", html);
        Assert.Contains("href=\"mailto:a@example.com\"", html);
        Assert.Contains("src=\"https://cms/media/abc/i.jpg\"", html);
        Assert.Contains("href=\"http://www.example.org\"", html);
    }

    [Fact]
    public void Excerpt_uses_paragraph_prose_and_cuts_at_a_word()
    {
        var document = MarkdownText.Parse("""
            # Heading is skipped

            First **bold** sentence with `code` and a [link](https://example.com).

            ```
            code block is skipped
            ```

            Second paragraph that goes on and on and on and on and on and on and on and on and on and on and on and on and on.
            """);

        var excerpt = MarkdownText.Excerpt(document, maxLength: 80);

        Assert.Equal("First bold sentence with code and a link. Second paragraph that goes on and on…", excerpt);
        Assert.Null(MarkdownText.Excerpt(MarkdownText.Parse("# Only a heading")));
    }

    [Fact]
    public void Reading_time_is_at_least_one_minute_and_ignores_code()
    {
        var prose = string.Join(' ', Enumerable.Repeat("word", 401));
        var code = string.Join(' ', Enumerable.Repeat("token", 2000));

        Assert.Equal(3, MarkdownText.ReadingTimeMinutes(MarkdownText.Parse($"{prose}\n\n```\n{code}\n```")));
        Assert.Equal(1, MarkdownText.ReadingTimeMinutes(MarkdownText.Parse(string.Empty)));
    }
}
