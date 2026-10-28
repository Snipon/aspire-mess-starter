using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Api.Content;

/// <summary>Renders CMS Markdown to HTML that is safe to insert as-is, and extracts plain text from it.</summary>
internal static class MarkdownText
{
    private const int WordsPerMinute = 200;

    private static readonly HashSet<string> SafeSchemes = new(["http", "https", "mailto", "tel"], StringComparer.OrdinalIgnoreCase);

    // CommonMark plus tables, strikethrough and bare-URL autolinks. Deliberately not UseAdvancedExtensions: generic
    // attributes ({onclick=...}) would let content inject event handlers. DisableHtml escapes raw HTML.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    public static MarkdownDocument Parse(string? markdown) => Markdown.Parse(markdown ?? string.Empty, Pipeline);

    /// <summary>
    /// Renders <paramref name="document"/> to HTML. Every link and image URL passes through <paramref name="rewriteUrl"/>
    /// and is replaced with <c>#</c> when it uses a scheme other than http(s), mailto or tel (e.g. <c>javascript:</c>).
    /// </summary>
    public static string ToHtml(MarkdownDocument document, Func<string, string>? rewriteUrl = null)
    {
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer)
        {
            LinkRewriter = url => IsSafeUrl(url) ? rewriteUrl?.Invoke(url) ?? url : "#",
        };
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>The prose of the paragraphs, cut at a word boundary to at most <paramref name="maxLength"/> characters.</summary>
    public static string? Excerpt(MarkdownDocument document, int maxLength = 160)
    {
        var text = PlainText(document.Descendants<ParagraphBlock>());
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', maxLength - 1);
        return text[..(cut > 0 ? cut : maxLength - 1)].TrimEnd(',', ';', ':', '.', ' ') + "…";
    }

    /// <summary>Reading time of the text (headings, paragraphs, lists, tables; not code blocks) in whole minutes, at least 1.</summary>
    public static int ReadingTimeMinutes(MarkdownDocument document)
    {
        var words = PlainText(document.Descendants<LeafBlock>())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Ceiling(words / (double)WordsPerMinute));
    }

    /// <summary>
    /// True for relative URLs and http(s)/mailto/tel. Whitespace and control characters are ignored when finding the
    /// scheme, as browsers do (<c>java&#9;script:</c>).
    /// </summary>
    public static bool IsSafeUrl(string url)
    {
        var normalized = string.Concat(url.Where(c => c > ' '));
        var end = normalized.IndexOfAny([':', '/', '?', '#']);
        return end < 0 || normalized[end] != ':' || SafeSchemes.Contains(normalized[..end]);
    }

    private static string PlainText(IEnumerable<LeafBlock> blocks)
    {
        var text = new StringBuilder();
        foreach (var block in blocks)
        {
            if (block.Inline is null) // code blocks keep their text in Lines, not inlines
            {
                continue;
            }

            foreach (var inline in block.Inline.Descendants<Inline>())
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        text.Append(literal.Content.AsSpan());
                        break;
                    case CodeInline code:
                        text.Append(code.Content);
                        break;
                    case LineBreakInline:
                        text.Append(' ');
                        break;
                }
            }

            text.Append(' ');
        }

        return string.Join(' ', text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
