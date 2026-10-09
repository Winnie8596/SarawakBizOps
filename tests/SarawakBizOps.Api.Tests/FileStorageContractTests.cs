using Azure.Storage.Blobs;
using SarawakBizOps.Api.Services.Storage;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Phase 8: one set of expectations for every <see cref="IFileStorage"/>. The fake runs it as a plain unit
/// test; the Azurite implementation runs it against the emulator container. Passing both is what lets tests
/// rely on the fake to stand in for real storage.
/// </summary>
public abstract class FileStorageContractTests
{
    protected abstract IFileStorage Storage { get; }

    private static string NewKey() => $"contract/{Guid.NewGuid():N}.png";

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task A_saved_file_reads_back_with_its_bytes_and_content_type()
    {
        var key = NewKey();
        var bytes = TestData.SignaturePng;

        await Storage.SaveAsync(key, new MemoryStream(bytes), "image/png", CancellationToken.None);
        var file = await Storage.OpenReadAsync(key, CancellationToken.None);

        Assert.NotNull(file);
        await using (file!.Content)
        {
            Assert.Equal(bytes, await ReadAllAsync(file.Content));
        }
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(bytes.Length, file.Length);
    }

    [Fact]
    public async Task Opening_a_key_that_was_never_saved_returns_null()
    {
        Assert.Null(await Storage.OpenReadAsync(NewKey(), CancellationToken.None));
    }

    [Fact]
    public async Task Listing_gives_the_files_under_a_prefix_and_when_each_was_written()
    {
        // 6.11: what the orphan sweep walks. The window allows for the emulator's clock being a little off.
        var folder = $"contract/{Guid.NewGuid():N}/";
        var before = DateTimeOffset.UtcNow.AddMinutes(-5);
        await Storage.SaveAsync(folder + "b.png", new MemoryStream(TestData.SignaturePng), "image/png", CancellationToken.None);
        await Storage.SaveAsync(folder + "a.png", new MemoryStream(TestData.SignaturePng), "image/png", CancellationToken.None);
        await Storage.SaveAsync($"contract/{Guid.NewGuid():N}/elsewhere.png", new MemoryStream(TestData.SignaturePng), "image/png", CancellationToken.None);

        var listed = new List<StoredFileInfo>();
        await foreach (var file in Storage.ListAsync(folder, CancellationToken.None))
        {
            listed.Add(file);
        }

        Assert.Equal(new[] { folder + "a.png", folder + "b.png" }, listed.Select(f => f.Key).Order());
        Assert.All(listed, f => Assert.InRange(f.LastModified, before, DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public async Task A_deleted_file_is_gone_and_deleting_again_is_not_an_error()
    {
        var key = NewKey();
        await Storage.SaveAsync(key, new MemoryStream(TestData.SignaturePng), "image/png", CancellationToken.None);

        await Storage.DeleteAsync(key, CancellationToken.None);
        await Storage.DeleteAsync(key, CancellationToken.None);

        Assert.Null(await Storage.OpenReadAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task Saving_twice_under_the_same_key_is_refused_and_keeps_the_first_file()
    {
        var key = NewKey();
        var first = TestData.SignaturePng;
        await Storage.SaveAsync(key, new MemoryStream(first), "image/png", CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Storage.SaveAsync(key, new MemoryStream(new byte[] { 1, 2, 3 }), "image/png", CancellationToken.None));

        var file = await Storage.OpenReadAsync(key, CancellationToken.None);
        await using (file!.Content)
        {
            Assert.Equal(first, await ReadAllAsync(file.Content));
        }
    }
}

public class InMemoryFileStorageContractTests : FileStorageContractTests
{
    protected override IFileStorage Storage { get; } = new InMemoryFileStorage();
}

/// <summary>The real implementation against the Azurite container the shared <see cref="ApiFactory"/> starts,
/// in a container of its own so it never touches the API's files.</summary>
[Collection(ApiCollection.Name)]
public class AzureBlobFileStorageContractTests : FileStorageContractTests
{
    public AzureBlobFileStorageContractTests(ApiFactory factory)
        => Storage = new AzureBlobFileStorage(
            new BlobContainerClient(factory.StorageConnectionString, $"contract-{Guid.NewGuid():N}"));

    protected override IFileStorage Storage { get; }
}
