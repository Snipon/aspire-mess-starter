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

    [GeneratedRegex("^[A-Za-z]{1,8}(-[A-Za-z0-9]{1,8})*$")]
    private static partial Regex CultureTag();
}
