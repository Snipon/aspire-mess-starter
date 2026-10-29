using System.Text;

namespace Cms.Headless;

/// <summary>A generated demo blog post. <see cref="Body"/> is Markdown; <see cref="Article"/> is the article it refers to.</summary>
internal sealed record DemoBlogPost(string Title, string Body, DemoArticle Article, DateTime PublishDate);

/// <summary>
/// <see cref="Count"/> deterministic demo blog posts (fixed seed), each referring to one of the
/// <see cref="DemoArticles"/> on the same subject. Titles combine a subject with one of the <see cref="TitleTemplates"/>,
/// so they never collide with article titles. Publish dates are about one every three days before <c>today</c>, newest first.
/// </summary>
internal static class DemoBlogPosts
{
    public const int Count = 500;

    // {0} is the subject in title case. Subjects * TitleTemplates.Length must be at least Count.
    private static readonly string[] TitleTemplates =
    [
        "Behind the Scenes: {0}", "Team Notes on {0}", "What Changed Our Mind About {0}", "Week in Review: {0}",
        "Reader Questions About {0}", "Our Take on {0}", "{0}: Quick Thoughts", "Pairing Session Recap: {0}",
        "Retro Notes: {0}", "Small Wins with {0}", "{0} Around the Office", "Hallway Track: {0}",
    ];

    private static readonly string[] Openers =
    [
        "This week we spent more time than planned on {0}, and it was worth it.",
        "A short post on {0}, prompted by a question from a reader.",
        "We keep coming back to {0} in our retrospectives, so here are some notes.",
        "Not a deep dive this time: just what we noticed while working with {0}.",
    ];

    public static IReadOnlyList<DemoBlogPost> Generate(DateTime today, IReadOnlyList<DemoArticle> articles)
    {
        var random = new Random(20261029);
        ILookup<string, DemoArticle> bySubject = articles.ToLookup(a => a.Subject);
        string[] subjects = [.. bySubject.Select(g => g.Key)];
        int[] combinations = [.. Enumerable.Range(0, subjects.Length * TitleTemplates.Length).OrderBy(_ => random.Next()).Take(Count)];
        var posts = new List<DemoBlogPost>(combinations.Length);
        for (var i = 0; i < combinations.Length; i++)
        {
            string subject = subjects[combinations[i] % subjects.Length];
            string title = string.Format(TitleTemplates[combinations[i] / subjects.Length], DemoArticles.TitleCase(subject));
            DemoArticle[] candidates = [.. bySubject[subject]];
            DemoArticle article = candidates[random.Next(candidates.Length)];
            DateTime publishDate = today.Date.AddDays(-(i * 3 + random.Next(0, 3)));
            posts.Add(new DemoBlogPost(title, Body(random, subject, article), article, publishDate));
        }

        return posts;
    }

    private static string Body(Random random, string subject, DemoArticle article)
    {
        var md = new StringBuilder();
        md.AppendLine(DemoArticles.Paragraph(random, string.Format(Openers[random.Next(Openers.Length)], subject), 3)).AppendLine();
        md.AppendLine("## What we tried").AppendLine();
        md.AppendLine(DemoArticles.Paragraph(random, null, random.Next(3, 5))).AppendLine();
        md.AppendLine("## Where to go from here").AppendLine();
        md.AppendLine(DemoArticles.Paragraph(random, $"For the longer version, read \"{article.Title}\".", random.Next(2, 4)));
        return md.ToString().TrimEnd() + "\n";
    }
}
