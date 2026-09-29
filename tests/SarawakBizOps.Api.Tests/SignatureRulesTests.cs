using System.Buffers.Binary;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.Services.WorkOrders;

namespace SarawakBizOps.Api.Tests;

/// <summary>Phase 8: what a signature upload must be, tested without a database or storage.</summary>
public class SignatureRulesTests
{
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // Just enough of a PNG for the header checks: signature, then an IHDR chunk with the given size.
    private static byte[] PngHeader(uint width, uint height, int totalLength = 64)
    {
        var bytes = new byte[totalLength];
        PngMagic.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    [Fact]
    public void A_real_png_from_the_signature_generator_is_accepted()
    {
        Assert.Null(SignatureRules.Validate(DemoSignatureImage.Create("any seed")));
    }

    [Fact]
    public void An_empty_upload_is_refused()
    {
        Assert.Contains("empty", SignatureRules.Validate(Array.Empty<byte>()));
    }

    [Fact]
    public void A_file_over_the_size_limit_is_refused_even_with_a_valid_header()
    {
        var tooBig = PngHeader(400, 150, SignatureRules.MaxBytes + 1);

        Assert.Contains("too large", SignatureRules.Validate(tooBig));
    }

    [Fact]
    public void A_file_exactly_at_the_limit_is_accepted()
    {
        Assert.Null(SignatureRules.Validate(PngHeader(400, 150, SignatureRules.MaxBytes)));
    }

    public static TheoryData<string, byte[]> NotPngs() => new()
    {
        { "JPEG", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0 } },
        { "text named .png", "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray() },
        { "PNG signature only", PngMagic },
        { "PNG signature without IHDR first", PngMagic.Concat(new byte[] { 0, 0, 0, 13 }).Concat("IDAT"u8.ToArray()).Concat(new byte[16]).ToArray() }
    };

    [Theory]
    [MemberData(nameof(NotPngs))]
    public void Anything_that_is_not_a_png_is_refused(string _, byte[] bytes)
    {
        Assert.Equal("The signature must be a PNG image.", SignatureRules.Validate(bytes));
    }

    [Theory]
    [InlineData(0u, 100u)]
    [InlineData(100u, 0u)]
    [InlineData(SignatureRules.MaxDimension + 1u, 100u)]
    [InlineData(100u, SignatureRules.MaxDimension + 1u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void Absurd_dimensions_are_refused(uint width, uint height)
    {
        Assert.Contains("pixels", SignatureRules.Validate(PngHeader(width, height)));
    }

    [Fact]
    public void Every_upload_gets_a_fresh_key_under_its_work_order()
    {
        var first = SignatureRules.NewKey(42);
        var second = SignatureRules.NewKey(42);

        Assert.NotEqual(first, second);
        Assert.StartsWith("work-orders/42/signature-", first);
        Assert.EndsWith(".png", first);
    }
}
