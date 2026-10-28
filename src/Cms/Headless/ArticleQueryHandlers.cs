using System.Text.Json;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.Models;

namespace Cms.Headless;

// Delivery API query extensions (discovered automatically; values come from the published version of the content):
//   sort=publishDate:desc   the article "publishDate" property, falling back to the create date (all content)
//   filter=tag:dotnet       articles tagged "dotnet" (comma-separated values match any, "tag:!x" excludes)

public sealed class PublishDateSortIndexer : IContentIndexHandler
{
    internal const string FieldName = "publishDate";

    public IEnumerable<IndexFieldValue> GetFieldValues(IContent content, string? culture) =>
        [new IndexFieldValue { FieldName = FieldName, Values = [content.GetValue<DateTime?>(FieldName, published: true) ?? content.CreateDate] }];

    public IEnumerable<IndexField> GetFields() =>
        [new IndexField { FieldName = FieldName, FieldType = FieldType.Date, VariesByCulture = false }];
}

public sealed class PublishDateSort : ISortHandler
{
    private const string Specifier = "publishDate:";

    public bool CanHandle(string query) => query.StartsWith(Specifier, StringComparison.OrdinalIgnoreCase);

    public SortOption BuildSortOption(string sort) => new()
    {
        FieldName = PublishDateSortIndexer.FieldName,
        Direction = sort[Specifier.Length..].StartsWith("asc", StringComparison.OrdinalIgnoreCase) ? Direction.Ascending : Direction.Descending,
    };
}

public sealed class TagFilterIndexer : IContentIndexHandler
{
    internal const string FieldName = "tag";

    public IEnumerable<IndexFieldValue> GetFieldValues(IContent content, string? culture)
    {
        // Tags are stored as a JSON array (Article Tags data type, storage type Json).
        string? json = content.GetValue<string>("tags", published: true);
        string[] tags = string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];
        return tags.Length == 0 ? [] : [new IndexFieldValue { FieldName = FieldName, Values = [.. tags.Select(t => t.ToLowerInvariant())] }];
    }

    public IEnumerable<IndexField> GetFields() =>
        [new IndexField { FieldName = FieldName, FieldType = FieldType.StringRaw, VariesByCulture = false }];
}

public sealed class TagFilter : IFilterHandler
{
    private const string Specifier = "tag:";

    public bool CanHandle(string query) => query.StartsWith(Specifier, StringComparison.OrdinalIgnoreCase);

    public FilterOption BuildFilterOption(string filter)
    {
        string value = filter[Specifier.Length..];
        bool negate = value.StartsWith('!');
        return new FilterOption
        {
            FieldName = TagFilterIndexer.FieldName,
            Values = [.. value.TrimStart('!').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => v.ToLowerInvariant())],
            Operator = negate ? FilterOperation.IsNot : FilterOperation.Is,
        };
    }
}
