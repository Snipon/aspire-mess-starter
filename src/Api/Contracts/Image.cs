namespace Api.Contracts;

/// <summary>An image with an absolute public URL and signed, resized renditions for <c>srcset</c>.</summary>
public sealed record Image(
    string Url,
    string? Alt,
    int? Width,
    int? Height,
    FocalPoint? FocalPoint,
    ImageRendition[] Srcset);

/// <summary>Focal point as fractions (0..1) of width and height.</summary>
public sealed record FocalPoint(double Left, double Top);

/// <summary>A resized rendition of an image.</summary>
public sealed record ImageRendition(int Width, string Url);
