namespace SarawakBizOps.Api.Services.WorkOrders;

/// <summary>
/// What counts as an acceptable job photo (Phase 9), as a pure class so every rule is unit-testable without a
/// database or storage. The type is decided from the bytes — JPEG or PNG — and the client's Content-Type and
/// file name are never trusted, so nothing else (SVG, HTML, an executable) can be stored and later served back as
/// an "image". WebP is recognised but refused (6.11): a PDF has no filter for it, so it could never appear in the
/// service report; the web app redraws a WebP as a JPEG before uploading it.
/// </summary>
public static class PhotoRules
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    /// <summary>Recognised so it can be refused by name; only photos stored before 6.11 have this type.</summary>
    public const string Webp = "image/webp";

    public const string WebpRefused =
        "WebP photos are not accepted because they cannot appear in the service report. Upload a JPEG or PNG instead.";

    public const string Unreadable = "This photo could not be read. Take it again or choose another photo.";

    /// <summary>A phone camera JPEG is typically 2–6 MB; the web app shrinks anything larger before sending.</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>Enough to document a job; bounds the storage one job can take (20 × 8 MB).</summary>
    public const int MaxPhotosPerJob = 20;

    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // The shortest header any of the three formats can be recognised from (WebP: "RIFF" size "WEBP").
    private const int MinHeaderBytes = 12;

    /// <summary>
    /// The photo's content type when <paramref name="bytes"/> is acceptable; otherwise the reason it is not.
    /// </summary>
    public static (string? ContentType, string? Error) Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return (null, "The photo is empty. Take or choose a photo, then upload it.");
        }

        if (bytes.Length > MaxBytes)
        {
            return (null, $"The photo is too large (maximum {MaxBytes / (1024 * 1024)} MB).");
        }

        var contentType = DetectContentType(bytes);
        return contentType switch
        {
            Jpeg or Png => (contentType, null),
            Webp => (null, WebpRefused),
            _ => (null, "A photo must be a JPEG or PNG image.")
        };
    }

    /// <summary>JPEG, PNG or WebP from the file's own signature bytes; null for anything else.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < MinHeaderBytes)
        {
            return null;
        }

        if (bytes.StartsWith(JpegMagic))
        {
            return Jpeg;
        }

        if (bytes.StartsWith(PngMagic) && bytes.Length >= 16 && bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return Png;
        }

        if (bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        return null;
    }

    /// <summary>A fresh, unguessable storage key for every upload; the extension follows the detected type.</summary>
    public static string NewKey(int workOrderId, string contentType)
    {
        var extension = contentType switch
        {
            Jpeg => "jpg",
            Png => "png",
            _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "Not a photo content type.")
        };
        return $"work-orders/{workOrderId}/photo-{Guid.NewGuid():N}.{extension}";
    }
}
