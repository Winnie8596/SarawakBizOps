using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.Services.WorkOrders;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>Phase 9: what a job photo upload must be, tested without a database or storage.</summary>
public class PhotoRulesTests
{
    public static TheoryData<string, byte[], string> Accepted() => new()
    {
        { "JPEG", PhotoJpeg, PhotoRules.Jpeg },
        { "PNG from the demo generator", DemoPhotoImage.Create("any seed"), PhotoRules.Png },
        { "WebP", PhotoWebp, PhotoRules.Webp }
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void Jpeg_png_and_webp_are_accepted_and_typed_from_their_bytes(string _, byte[] bytes, string expectedType)
    {
        Assert.Equal((expectedType, (string?)null), PhotoRules.Validate(bytes));
    }

    [Fact]
    public void An_empty_upload_is_refused()
    {
        var (contentType, error) = PhotoRules.Validate(Array.Empty<byte>());

        Assert.Null(contentType);
        Assert.Contains("empty", error);
    }

    [Fact]
    public void A_file_over_the_size_limit_is_refused_even_with_a_valid_header()
    {
        var tooBig = new byte[PhotoRules.MaxBytes + 1];
        PhotoJpeg.CopyTo(tooBig, 0);

        Assert.Contains("too large", PhotoRules.Validate(tooBig).Error);
    }

    [Fact]
    public void A_file_exactly_at_the_limit_is_accepted()
    {
        var atLimit = new byte[PhotoRules.MaxBytes];
        PhotoJpeg.CopyTo(atLimit, 0);

        Assert.Equal(PhotoRules.Jpeg, PhotoRules.Validate(atLimit).ContentType);
    }

    public static TheoryData<string, byte[]> NotPhotos() => new()
    {
        { "SVG with a script", "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray() },
        { "HTML", "<!doctype html><html><body>hi</body></html>"u8.ToArray() },
        { "GIF", "GIF89a"u8.ToArray().Concat(new byte[16]).ToArray() },
        { "PDF", "%PDF-1.7\n%âãÏÓ"u8.ToArray().Concat(new byte[16]).ToArray() },
        { "RIFF that is not WebP (a WAV file)", "RIFF"u8.ToArray().Concat(new byte[4]).Concat("WAVEfmt "u8.ToArray()).Concat(new byte[16]).ToArray() },
        { "PNG signature without IHDR first", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.Concat("IDAT"u8.ToArray()).Concat(new byte[16]).ToArray() },
        { "a JPEG marker alone, too short to be an image", new byte[] { 0xFF, 0xD8, 0xFF } }
    };

    [Theory]
    [MemberData(nameof(NotPhotos))]
    public void Anything_that_is_not_a_jpeg_png_or_webp_is_refused(string _, byte[] bytes)
    {
        Assert.Equal((null, "A photo must be a JPEG, PNG or WebP image."), PhotoRules.Validate(bytes));
    }

    [Fact]
    public void Every_upload_gets_a_new_key_under_its_job_with_an_extension_matching_its_type()
    {
        var jpeg = PhotoRules.NewKey(42, PhotoRules.Jpeg);
        var again = PhotoRules.NewKey(42, PhotoRules.Jpeg);

        Assert.NotEqual(jpeg, again);
        Assert.Matches(@"^work-orders/42/photo-[0-9a-f]{32}\.jpg$", jpeg);
        Assert.EndsWith(".png", PhotoRules.NewKey(42, PhotoRules.Png));
        Assert.EndsWith(".webp", PhotoRules.NewKey(42, PhotoRules.Webp));
        Assert.Throws<ArgumentOutOfRangeException>(() => PhotoRules.NewKey(42, "image/svg+xml"));
    }
}
