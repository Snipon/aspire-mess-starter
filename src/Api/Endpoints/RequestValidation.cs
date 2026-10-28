using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Api.Endpoints;

internal static partial class RequestValidation
{
    /// <summary>
    /// A content path: leading '/', no '.'/'..' segments (they would be collapsed by <see cref="Uri"/> and escape the
    /// content route on the CMS), no backslashes or control characters.
    /// </summary>
    public static bool IsValidPath([NotNullWhen(true)] string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.StartsWith('/')
        && path.Length <= 1024
        && !path.Any(c => char.IsControl(c) || c == '\\')
        && path.Split('/').All(segment => segment is not ("." or ".."));

    /// <summary>A BCP 47-ish culture tag (e.g. <c>en</c>, <c>en-US</c>, <c>zh-Hant-TW</c>); <c>null</c> is valid.</summary>
    public static bool IsValidCulture(string? culture) => culture is null || CultureTag().IsMatch(culture);

    public const string CultureError = "'culture' must be a culture tag such as 'en-US'.";

    public const int MaxPage = 1000;
    public const int MaxPageSize = 50;

    public static bool IsValidPaging(int page, int pageSize) => page is >= 1 and <= MaxPage && pageSize is >= 1 and <= MaxPageSize;

    public static readonly string PagingError = $"'page' must be between 1 and {MaxPage} and 'pageSize' between 1 and {MaxPageSize}.";

    public const int MaxTagLength = 200;

    /// <summary>
    /// A single tag as editors type it (e.g. <c>C#</c>, <c>CI/CD</c>): not padded, no control characters, no ',' and
    /// no leading '!' (CMS tag filter syntax for "any of" and "not"); <c>null</c> is valid.
    /// </summary>
    public static bool IsValidTag(string? tag) =>
        tag is null
        || (tag.Length is > 0 and <= MaxTagLength
            && tag.Trim().Length == tag.Length
            && !tag.StartsWith('!')
            && !tag.Any(c => c == ',' || char.IsControl(c)));

    public static readonly string TagError = $"'tag' must be a single tag of at most {MaxTagLength} characters, without ',' or a leading '!'.";

    [GeneratedRegex("^[A-Za-z]{1,8}(-[A-Za-z0-9]{1,8})*$")]
    private static partial Regex CultureTag();
}
