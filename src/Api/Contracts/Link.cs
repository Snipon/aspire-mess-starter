namespace Api.Contracts;

/// <summary>
/// A link. <see cref="Url"/> is a site path (e.g. <c>/articles</c>) for links to content, otherwise an absolute URL.
/// </summary>
/// <param name="Target">The link target (e.g. <c>_blank</c>), if set.</param>
public sealed record Link(string Url, string? Title, string? Target);
