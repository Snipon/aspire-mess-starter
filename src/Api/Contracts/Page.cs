using System.Text.Json;

namespace Api.Contracts;

/// <summary>A published content page, independent of the CMS that produced it.</summary>
public sealed record Page(
    string Id,
    string ContentType,
    string Path,
    string Name,
    string Title,
    string? Summary,
    string? BodyHtml,
    Image? Image,
    string? Culture,
    DateTimeOffset UpdatedAt,
    Dictionary<string, JsonElement> Properties);
