namespace Api.Contracts;

/// <summary>A video with an absolute public URL and its MIME type (for <c>&lt;source type&gt;</c>).</summary>
public sealed record Video(string Url, string MimeType);
