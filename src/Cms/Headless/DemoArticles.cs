using System.Text;

namespace Cms.Headless;

/// <summary>A generated demo article. <see cref="Body"/> is Markdown.</summary>
internal sealed record DemoArticle(string Title, string Body, string[] Tags, DateTime PublishDate);

/// <summary>
/// Deterministic demo articles (fixed seed) about web development, design and cloud: the same titles, bodies and tags
/// on every cold install. Publish dates are spread over the 12 months before <c>today</c>.
/// </summary>
internal static class DemoArticles
{
    public static readonly string[] TagPool =
    [
        "dotnet", "aspire", "azure", "cloud", "devops", "frontend",
        "css", "typescript", "accessibility", "performance", "security", "design-systems",
    ];

    // Title, subject used in the copy, and two primary tags (more are added at random).
    private static readonly (string Title, string Subject, string Tag1, string Tag2)[] Topics =
    [
        ("Getting Started with .NET Aspire for Local Development", "Aspire", "aspire", "dotnet"),
        ("Orchestrating Containers Without Docker Compose", "container orchestration", "aspire", "devops"),
        ("Service Discovery Patterns in Distributed .NET Apps", "service discovery", "dotnet", "cloud"),
        ("A Practical Guide to OpenTelemetry in ASP.NET Core", "OpenTelemetry", "dotnet", "devops"),
        ("Minimal APIs That Stay Maintainable", "minimal APIs", "dotnet", "performance"),
        ("Typed HTTP Clients from OpenAPI Documents", "OpenAPI clients", "typescript", "dotnet"),
        ("Deploying to Azure Container Apps with Terraform", "Azure Container Apps", "azure", "devops"),
        ("Managing Secrets with Key Vault References", "secret management", "azure", "security"),
        ("Zero-Downtime Database Migrations", "database migrations", "devops", "cloud"),
        ("Cost-Aware Architecture on the Cloud", "cloud cost control", "cloud", "azure"),
        ("Headless CMS Content Modelling Done Right", "content modelling", "design-systems", "frontend"),
        ("Server-Side Rendering with Astro", "server-side rendering", "frontend", "performance"),
        ("Islands Architecture Explained", "islands architecture", "frontend", "performance"),
        ("Designing Accessible Navigation Menus", "accessible navigation", "accessibility", "frontend"),
        ("Colour Contrast Beyond the Checklist", "colour contrast", "accessibility", "design-systems"),
        ("Building a Token-Based Design System", "design tokens", "design-systems", "css"),
        ("Tailwind CSS v4: What Changed and Why", "Tailwind CSS v4", "css", "frontend"),
        ("Container Queries in Production", "container queries", "css", "frontend"),
        ("Modern CSS Layout with Subgrid", "subgrid", "css", "design-systems"),
        ("Fluid Typography Without Media Queries", "fluid typography", "css", "accessibility"),
        ("Strict TypeScript in Large Codebases", "strict TypeScript", "typescript", "frontend"),
        ("Type-Safe Data Fetching End to End", "type-safe fetching", "typescript", "dotnet"),
        ("Discriminated Unions for UI State", "discriminated unions", "typescript", "frontend"),
        ("Core Web Vitals for Content Sites", "Core Web Vitals", "performance", "frontend"),
        ("Responsive Images with srcset and Signed URLs", "responsive images", "performance", "security"),
        ("Caching Strategies for Headless APIs", "API caching", "performance", "cloud"),
        ("Full-Text Search with Meilisearch", "full-text search", "performance", "devops"),
        ("Keeping a Search Index in Sync with Webhooks", "webhook-driven indexing", "devops", "dotnet"),
        ("Securing Webhooks with Shared Secrets", "webhook security", "security", "dotnet"),
        ("Content Security Policy for Server-Rendered Sites", "Content Security Policy", "security", "frontend"),
        ("HMAC Signing for Image Processing Endpoints", "HMAC signing", "security", "performance"),
        ("Threat Modelling for Small Teams", "threat modelling", "security", "devops"),
        ("Infrastructure as Code Reviews That Catch Problems", "infrastructure reviews", "devops", "azure"),
        ("Health Checks That Actually Mean Something", "health checks", "devops", "dotnet"),
        ("Structured Logging with Serilog", "structured logging", "dotnet", "devops"),
        ("Feature Flags Without the Chaos", "feature flags", "devops", "cloud"),
        ("Blue-Green Releases on Container Apps", "blue-green releases", "azure", "devops"),
        ("Scaling to Zero: When It Helps and When It Hurts", "scale to zero", "cloud", "performance"),
        ("Managed Identities in Practice", "managed identities", "azure", "security"),
        ("Designing Error States Users Understand", "error states", "design-systems", "accessibility"),
        ("Motion Design and Reduced Motion Preferences", "reduced motion", "accessibility", "css"),
        ("Form Validation That Respects Users", "form validation", "accessibility", "typescript"),
        ("Component APIs That Age Well", "component APIs", "design-systems", "typescript"),
        ("Documenting a Design System for Developers", "design system documentation", "design-systems", "frontend"),
        ("Dark Mode with CSS Custom Properties", "dark mode", "css", "design-systems"),
        ("Edge Rendering vs. Regional Rendering", "edge rendering", "cloud", "performance"),
        ("Testing Minimal APIs with WebApplicationFactory", "API integration tests", "dotnet", "devops"),
        ("Contract Testing Between Front End and API", "contract testing", "typescript", "devops"),
        ("Preview Environments for Every Pull Request", "preview environments", "devops", "azure"),
        ("From Monolith to Modular Without a Rewrite", "modular architecture", "dotnet", "cloud"),
    ];

    private static readonly string[] Openers =
    [
        "Teams adopt {0} for different reasons, but the goal is almost always the same: ship faster without losing control.",
        "It is easy to underestimate {0} until a production incident makes its value obvious.",
        "{0} has moved from a niche practice to something most modern teams rely on every day.",
        "If you have ever inherited a project, you know how much {0} decisions shape the daily experience.",
    ];

    private static readonly string[] Sentences =
    [
        "The trick is to start small and let real usage tell you where to invest next.",
        "Most of the complexity comes from edge cases, not from the happy path.",
        "A good default removes a whole category of decisions from code review.",
        "Measure before and after every change, otherwise you are optimising by intuition.",
        "Documentation matters here, because the next person will not have your context.",
        "Automation turns a fragile checklist into a repeatable step in the pipeline.",
        "Keep the boundaries explicit so each part can evolve on its own schedule.",
        "When in doubt, prefer the boring solution that the whole team understands.",
        "Treat configuration as part of the product, with the same care as the code.",
        "Small, frequent changes are easier to review, test and roll back.",
        "Feedback loops measured in seconds change how people work.",
        "The cost of a mistake is lowest when it is caught on a developer machine.",
        "Naming things consistently pays off every time someone searches the codebase.",
        "A clear contract between systems is worth more than any clever implementation.",
        "Users rarely notice good engineering, but they always notice its absence.",
        "Make the right thing the easy thing, and most people will do it.",
        "Observability is not an afterthought; it is how you learn what the system really does.",
        "Write down the trade-offs you accepted so they can be revisited later.",
        "Security and usability are not opposites when the defaults are sensible.",
        "Every abstraction should earn its place by removing more complexity than it adds.",
    ];

    private static readonly string[] SectionTitles =
    [
        "Why it matters", "The core idea", "A minimal setup", "Common pitfalls",
        "Making it repeatable", "Measuring the impact", "Going to production", "What to try next",
    ];

    private static readonly string[] ListItems =
    [
        "Start with the smallest useful version",
        "Automate the checks you would otherwise forget",
        "Keep secrets out of source control",
        "Document the decision, not just the outcome",
        "Add telemetry before you need it",
        "Review defaults with the whole team",
        "Prefer explicit configuration over conventions nobody remembers",
        "Test the failure paths, not only the success paths",
        "Budget time for clean-up after the first release",
        "Share what you learn in a short write-up",
    ];

    private static readonly Dictionary<string, (string Language, string Code)> Snippets = new()
    {
        ["dotnet"] = ("csharp", "var builder = WebApplication.CreateBuilder(args);\nbuilder.AddServiceDefaults();\n\nvar app = builder.Build();\napp.MapGet(\"/hello\", () => Results.Ok(new { message = \"Hello\" }));\napp.Run();"),
        ["aspire"] = ("csharp", "var builder = DistributedApplication.CreateBuilder(args);\nvar db = builder.AddSqlServer(\"sql\").AddDatabase(\"app\");\nbuilder.AddProject<Projects.Api>(\"api\").WithReference(db).WaitFor(db);\nbuilder.Build().Run();"),
        ["azure"] = ("hcl", "resource \"azurerm_container_app\" \"api\" {\n  name                         = \"api\"\n  container_app_environment_id = azurerm_container_app_environment.main.id\n  resource_group_name          = azurerm_resource_group.main.name\n  revision_mode                = \"Single\"\n}"),
        ["cloud"] = ("bash", "az containerapp revision list \\\n  --name api --resource-group rg-demo \\\n  --query \"[].{name:name, active:properties.active}\" -o table"),
        ["devops"] = ("yaml", "jobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v4\n      - run: dotnet test --configuration Release"),
        ["frontend"] = ("astro", "---\nconst { title } = Astro.props;\n---\n<article>\n  <h1>{title}</h1>\n  <slot />\n</article>"),
        ["css"] = ("css", ".card {\n  container-type: inline-size;\n  padding: clamp(1rem, 3cqi, 2rem);\n  border-radius: var(--radius-lg);\n}"),
        ["typescript"] = ("ts", "type LoadState<T> =\n  | { status: \"loading\" }\n  | { status: \"error\"; error: Error }\n  | { status: \"ready\"; data: T };"),
        ["accessibility"] = ("html", "<nav aria-label=\"Main\">\n  <ul>\n    <li><a href=\"/\" aria-current=\"page\">Home</a></li>\n    <li><a href=\"/articles/\">Articles</a></li>\n  </ul>\n</nav>"),
        ["performance"] = ("html", "<img\n  src=\"/media/hero-960.jpg\"\n  srcset=\"/media/hero-480.jpg 480w, /media/hero-960.jpg 960w, /media/hero-1920.jpg 1920w\"\n  sizes=\"(min-width: 60rem) 60rem, 100vw\"\n  loading=\"lazy\" alt=\"\" />"),
        ["security"] = ("csharp", "var expected = Convert.FromBase64String(secret);\nvar actual = HMACSHA256.HashData(expected, payload);\nreturn CryptographicOperations.FixedTimeEquals(actual, signature);"),
        ["design-systems"] = ("css", ":root {\n  --color-surface: oklch(98% 0.01 250);\n  --color-text: oklch(25% 0.02 250);\n  --space-4: 1rem;\n}"),
    };

    private static readonly Dictionary<string, (string Text, string Url)> Links = new()
    {
        ["dotnet"] = (".NET documentation", "https://learn.microsoft.com/dotnet/"),
        ["aspire"] = ("Aspire documentation", "https://aspire.dev"),
        ["azure"] = ("Azure Container Apps overview", "https://learn.microsoft.com/azure/container-apps/overview"),
        ["cloud"] = ("Azure Architecture Center", "https://learn.microsoft.com/azure/architecture/"),
        ["devops"] = ("GitHub Actions documentation", "https://docs.github.com/actions"),
        ["frontend"] = ("Astro documentation", "https://docs.astro.build"),
        ["css"] = ("MDN CSS reference", "https://developer.mozilla.org/docs/Web/CSS"),
        ["typescript"] = ("TypeScript handbook", "https://www.typescriptlang.org/docs/handbook/intro.html"),
        ["accessibility"] = ("WCAG 2.2 quick reference", "https://www.w3.org/WAI/WCAG22/quickref/"),
        ["performance"] = ("web.dev on Core Web Vitals", "https://web.dev/articles/vitals"),
        ["security"] = ("OWASP Cheat Sheet Series", "https://cheatsheetseries.owasp.org"),
        ["design-systems"] = ("Design Tokens Community Group", "https://www.designtokens.org"),
    };

    public static IReadOnlyList<DemoArticle> Generate(DateTime today)
    {
        var random = new Random(20261028);
        var articles = new List<DemoArticle>(Topics.Length);
        for (var i = 0; i < Topics.Length; i++)
        {
            (string title, string subject, string tag1, string tag2) = Topics[i];
            var tags = new List<string> { tag1, tag2 };
            int extra = random.Next(0, 3);
            while (tags.Count < 2 + extra)
            {
                string tag = TagPool[random.Next(TagPool.Length)];
                if (!tags.Contains(tag))
                {
                    tags.Add(tag);
                }
            }

            // Roughly one article a week, newest first, all within the last 12 months.
            DateTime publishDate = today.Date.AddDays(-(i * 7 + random.Next(0, 7)));
            articles.Add(new DemoArticle(title, Body(random, subject, tags), [.. tags], publishDate));
        }

        return articles;
    }

    private static string Body(Random random, string subject, List<string> tags)
    {
        var md = new StringBuilder();
        md.AppendLine(Paragraph(random, string.Format(Openers[random.Next(Openers.Length)], subject), 3)).AppendLine();
        md.AppendLine(Paragraph(random, $"This article looks at {subject} from a practical angle.", 3)).AppendLine();

        string[] sections = [.. SectionTitles.OrderBy(_ => random.Next()).Take(random.Next(3, 5))];
        for (var s = 0; s < sections.Length; s++)
        {
            md.AppendLine($"## {sections[s]}").AppendLine();
            md.AppendLine(Paragraph(random, null, random.Next(3, 6))).AppendLine();

            if (s == 0)
            {
                foreach (string item in ListItems.OrderBy(_ => random.Next()).Take(random.Next(3, 6)))
                {
                    md.AppendLine($"- {item}");
                }

                md.AppendLine();
            }
            else if (s == 1)
            {
                (string language, string code) = Snippets[tags[random.Next(tags.Count)]];
                md.AppendLine($"```{language}").AppendLine(code).AppendLine("```").AppendLine();
            }

            md.AppendLine(Paragraph(random, null, random.Next(2, 5))).AppendLine();
        }

        md.AppendLine("## Further reading").AppendLine();
        foreach (string tag in tags)
        {
            (string text, string url) = Links[tag];
            md.AppendLine($"- [{text}]({url})");
        }

        return md.ToString().TrimEnd() + "\n";
    }

    private static string Paragraph(Random random, string? first, int sentences)
    {
        IEnumerable<string> picked = Sentences.OrderBy(_ => random.Next()).Take(sentences);
        return string.Join(' ', first is null ? picked : picked.Prepend(first));
    }
}
