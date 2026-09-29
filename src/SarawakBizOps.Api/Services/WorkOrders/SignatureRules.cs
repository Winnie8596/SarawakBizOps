using System.Buffers.Binary;

namespace SarawakBizOps.Api.Services.WorkOrders;

/// <summary>
/// What counts as an acceptable customer signature upload (Phase 8, BR-03), as a pure class so every rule
/// is unit-testable without a database or storage. The file must really be a PNG — the bytes are checked,
/// the client's Content-Type and file name are never trusted — and small enough for a canvas drawing.
/// </summary>
public static class SignatureRules
{
    public const string ContentType = "image/png";

    /// <summary>A 360–1000 px canvas of pen strokes compresses to a few KB–tens of KB; 512 KB is generous.</summary>
    public const int MaxBytes = 512 * 1024;

    /// <summary>Guards against a "PNG" of absurd dimensions (a decompression bomb for whoever renders it).</summary>
    public const int MaxDimension = 4000;

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // PNG signature (8) + IHDR length (4) + "IHDR" (4) + width (4) + height (4).
    private const int MinHeaderBytes = 24;

    /// <summary>Null when <paramref name="bytes"/> is an acceptable signature image, otherwise why not.</summary>
    public static string? Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return "The signature is empty. Ask the customer to sign, then save it.";
        }

        if (bytes.Length > MaxBytes)
        {
            return $"The signature image is too large (maximum {MaxBytes / 1024} KB).";
        }

        if (bytes.Length < MinHeaderBytes
            || !bytes[..PngSignature.Length].SequenceEqual(PngSignature)
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return "The signature must be a PNG image.";
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension)
        {
            return $"The signature image must be between 1 and {MaxDimension} pixels on each side.";
        }

        return null;
    }

    /// <summary>A fresh, unguessable storage key for every upload, so a re-sign never overwrites the file the
    /// database still points at until the new one is safely recorded.</summary>
    public static string NewKey(int workOrderId)
        => $"work-orders/{workOrderId}/signature-{Guid.NewGuid():N}.png";
}
