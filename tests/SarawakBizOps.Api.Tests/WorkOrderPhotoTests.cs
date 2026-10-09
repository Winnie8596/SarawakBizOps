using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Reports.Pdf;
using SarawakBizOps.Api.Services.Storage;
using SarawakBizOps.Api.Services.WorkOrders;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Phase 9: photos taken on a job (PRD §6.6, SDD §15) — stored through IFileStorage (Azurite here), typed from
/// their bytes, removable only before completion, readable only through the authorized download endpoint.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkOrderPhotoTests
{
    private readonly ApiFactory _factory;

    public WorkOrderPhotoTests(ApiFactory factory) => _factory = factory;

    private static async Task<string> DetailAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail ?? string.Empty;

    private async Task<(HttpClient Admin, HttpClient Manager, HttpClient Technician, TestUser User, WorkOrderDto Job)> InProgressJobAsync()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technician, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var job = await CreateInProgressWorkOrderAsync(admin, manager, technician, user.Id);
        return (admin, manager, technician, user, job);
    }

    private async Task<List<string>> StoredKeysAsync(int workOrderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.WorkOrderPhotos.Where(p => p.WorkOrderId == workOrderId).OrderBy(p => p.Id).Select(p => p.BlobKey).ToListAsync();
    }

    // ---- Uploading and reading back ----

    [Fact]
    public async Task Photos_are_stored_in_blob_storage_and_served_back_byte_for_byte_with_their_real_type()
    {
        var (admin, manager, technician, _, job) = await InProgressJobAsync();
        var staff = await _factory.ClientWithRoleAsync(AppRoles.ServiceStaff);

        await UploadPhotoAsync(technician, job.Id, PhotoJpeg);
        var response = await PostPhotoAsync(technician, job.Id, PhotoPng);
        var json = await response.Content.ReadAsStringAsync();
        var withPhotos = (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;

        // Neither has metadata to strip (6.11), so both are stored exactly as sent.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "image/jpeg", "image/png" }, withPhotos.Photos.Select(p => p.ContentType));
        Assert.Equal(new[] { PhotoJpeg.Length, PhotoPng.Length }, withPhotos.Photos.Select(p => p.SizeBytes));
        Assert.All(withPhotos.Photos, p => Assert.False(string.IsNullOrEmpty(p.UploadedByName)));
        Assert.NotEqual(job.RowVersion, withPhotos.RowVersion); // like every mutating call, it bumps the token

        // The DTO says what photos exist — never where the files live.
        var keys = await StoredKeysAsync(job.Id);
        Assert.Equal(2, keys.Count);
        Assert.All(keys, key => Assert.DoesNotContain(key, json));
        Assert.DoesNotContain("blob", json, StringComparison.OrdinalIgnoreCase);
        var storage = _factory.Services.GetRequiredService<IFileStorage>();
        foreach (var key in keys)
        {
            Assert.NotNull(await storage.OpenReadAsync(key, CancellationToken.None));
        }

        var expected = new[] { (PhotoJpeg, "image/jpeg"), (PhotoPng, "image/png") };
        foreach (var client in new[] { technician, manager, admin, staff })
        {
            for (var i = 0; i < expected.Length; i++)
            {
                var download = await client.GetAsync($"/api/work-orders/{job.Id}/photos/{withPhotos.Photos[i].Id}");
                Assert.Equal(HttpStatusCode.OK, download.StatusCode);
                Assert.Equal(expected[i].Item2, download.Content.Headers.ContentType!.MediaType);
                Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));
                Assert.True(download.Headers.CacheControl!.NoStore);
                Assert.Equal(expected[i].Item1, await download.Content.ReadAsByteArrayAsync());
            }
        }
    }

    [Fact]
    public async Task A_real_photo_is_accepted_and_served_as_what_it_is_whatever_the_browser_called_it()
    {
        var (_, manager, technician, _, job) = await InProgressJobAsync();

        var response = await PostPhotoAsync(technician, job.Id, PhotoJpeg, "IMG_0001.HEIC", "application/octet-stream");
        var photo = Assert.Single((await response.Content.ReadFromJsonAsync<WorkOrderDto>())!.Photos);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", photo.ContentType);
        var download = await manager.GetAsync($"/api/work-orders/{job.Id}/photos/{photo.Id}");
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType!.MediaType);
    }

    // ---- File type and size limits ----

    public static TheoryData<string, byte[], string, string> BadFiles() => new()
    {
        { "SVG with a script declared as JPEG", "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray(), "photo.jpg", "image/jpeg" },
        { "HTML declared as PNG", "<!doctype html><html><script>alert(1)</script></html>"u8.ToArray(), "photo.png", "image/png" },
        { "GIF", "GIF89a"u8.ToArray().Concat(new byte[32]).ToArray(), "photo.gif", "image/gif" },
        { "plain text", "not an image at all"u8.ToArray(), "notes.txt", "text/plain" }
    };

    [Theory]
    [MemberData(nameof(BadFiles))]
    public async Task Anything_but_a_jpeg_or_png_is_refused_whatever_it_claims_to_be(
        string _, byte[] bytes, string fileName, string contentType)
    {
        var (_, _, _, user, job) = await InProgressJobAsync();
        var storage = new InMemoryFileStorage();
        await using var host = _factory.WithFileStorage(storage);
        var technician = await host.SignInAsync(user);

        var response = await PostPhotoAsync(technician, job.Id, bytes, fileName, contentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A photo must be a JPEG or PNG image.", await DetailAsync(response));
        Assert.Empty(storage.Keys); // refused before anything reached storage
        await AssertNoPhotosAsync(technician, job);
    }

    // ---- 6.11: no WebP, and no metadata ----

    [Fact]
    public async Task A_webp_is_refused_by_name_because_it_could_not_appear_in_the_report()
    {
        var (_, _, _, user, job) = await InProgressJobAsync();
        var storage = new InMemoryFileStorage();
        await using var host = _factory.WithFileStorage(storage);
        var technician = await host.SignInAsync(user);

        var response = await PostPhotoAsync(technician, job.Id, PhotoWebp, "photo.webp", "image/webp");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PhotoRules.WebpRefused, await DetailAsync(response));
        Assert.Empty(storage.Keys);
        await AssertNoPhotosAsync(technician, job);
    }

    [Fact]
    public async Task A_phone_photo_is_stored_without_its_location_or_camera_details_but_keeps_its_orientation()
    {
        var (_, manager, technician, _, job) = await InProgressJobAsync();
        var original = PhotoMetadataTests.PhoneJpeg(orientation: 6);

        var photo = Assert.Single((await UploadPhotoAsync(technician, job.Id, original)).Photos);
        var served = await manager.GetByteArrayAsync($"/api/work-orders/{job.Id}/photos/{photo.Id}");

        Assert.Equal(PhotoMetadata.Strip(original, PhotoRules.Jpeg), served);
        Assert.Equal(served.Length, photo.SizeBytes); // the size of what is stored, not of what was sent
        Assert.All(PhotoMetadataTests.Secrets, secret => Assert.DoesNotContain(secret, Encoding.Latin1.GetString(served)));
        Assert.Equal(6, PdfImage.FromJpeg(served)!.Orientation);
    }

    [Fact]
    public async Task A_photo_too_damaged_to_clean_is_refused_and_nothing_is_stored()
    {
        var (_, _, _, user, job) = await InProgressJobAsync();
        var storage = new InMemoryFileStorage();
        await using var host = _factory.WithFileStorage(storage);
        var technician = await host.SignInAsync(user);
        var damaged = PhotoMetadataTests.PhoneJpeg(orientation: 6);
        damaged[4] = 0xFF; // the first segment claims to run past the end of the file
        damaged[5] = 0xF0;

        var response = await PostPhotoAsync(technician, job.Id, damaged, "photo.jpg", "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PhotoRules.Unreadable, await DetailAsync(response));
        Assert.Empty(storage.Keys);
        await AssertNoPhotosAsync(technician, job);
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused()
    {
        var (_, _, technician, _, job) = await InProgressJobAsync();
        var tooBig = new byte[PhotoRules.MaxBytes + 1];
        PhotoJpeg.CopyTo(tooBig, 0);

        var response = await PostPhotoAsync(technician, job.Id, tooBig, "big.jpg", "image/jpeg");

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            $"Expected 400 or 413, got {(int)response.StatusCode}");
        await AssertNoPhotosAsync(technician, job);
    }

    [Fact]
    public async Task A_request_far_over_the_limit_is_cut_off_before_it_is_buffered()
    {
        var (_, _, technician, _, job) = await InProgressJobAsync();

        var response = await PostPhotoAsync(technician, job.Id, new byte[20 * 1024 * 1024], "huge.jpg", "image/jpeg");

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            $"Expected 400 or 413, got {(int)response.StatusCode}");
        await AssertNoPhotosAsync(technician, job);
    }

    [Fact]
    public async Task A_form_without_a_file_is_refused()
    {
        var (_, _, technician, _, job) = await InProgressJobAsync();

        using var form = PhotoForm(null, job.RowVersion);
        var response = await technician.PostAsync($"/api/work-orders/{job.Id}/photos", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("empty", await DetailAsync(response));
        await AssertNoPhotosAsync(technician, job);
    }

    [Fact]
    public async Task A_job_holds_at_most_the_photo_limit()
    {
        var (_, _, technician, _, job) = await InProgressJobAsync();
        for (var i = 0; i < PhotoRules.MaxPhotosPerJob; i++)
        {
            await UploadPhotoAsync(technician, job.Id, PhotoJpeg);
        }

        var refused = await PostPhotoAsync(technician, job.Id, PhotoJpeg);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains($"already has {PhotoRules.MaxPhotosPerJob} photos", await DetailAsync(refused));
        Assert.Equal(PhotoRules.MaxPhotosPerJob, (await GetWorkOrderAsync(technician, job.Id))!.Photos.Count);
    }

    // ---- Removing before completion ----

    [Fact]
    public async Task Removing_a_photo_deletes_the_row_and_the_stored_file()
    {
        var (_, _, technician, _, job) = await InProgressJobAsync();
        await UploadPhotoAsync(technician, job.Id, PhotoJpeg);
        var two = await UploadPhotoAsync(technician, job.Id, PhotoPng);
        var keys = await StoredKeysAsync(job.Id);

        var response = await DeletePhotoAsync(technician, job.Id, two.Photos[0].Id);
        var after = (await response.Content.ReadFromJsonAsync<WorkOrderDto>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(two.Photos[1].Id, Assert.Single(after.Photos).Id);
        Assert.NotEqual(two.RowVersion, after.RowVersion);
        var storage = _factory.Services.GetRequiredService<IFileStorage>();
        Assert.Null(await storage.OpenReadAsync(keys[0], CancellationToken.None));
        Assert.NotNull(await storage.OpenReadAsync(keys[1], CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound,
            (await technician.GetAsync($"/api/work-orders/{job.Id}/photos/{two.Photos[0].Id}")).StatusCode);
    }

    [Fact]
    public async Task Once_the_job_is_completed_its_photos_can_no_longer_be_removed_and_stay_readable()
    {
        var (_, manager, technician, _, job) = await InProgressJobAsync();
        await SaveDiagnosisAsync(technician, job.Id);
        var withPhoto = await UploadPhotoAsync(technician, job.Id);
        await UploadSignatureAsync(technician, job.Id);
        var completed = await CompleteWorkOrderAsync(technician, job.Id);
        var photoId = Assert.Single(completed.Photos).Id; // completing keeps the photos
        Assert.Equal(withPhoto.Photos.Single().Id, photoId);

        var afterComplete = await DeletePhotoAsync(technician, job.Id, photoId);
        await GenerateReportAsync(manager, job.Id);
        await ApproveWorkOrderAsync(manager, job.Id);
        var afterApprove = await DeletePhotoAsync(technician, job.Id, photoId);

        Assert.Equal(HttpStatusCode.Conflict, afterComplete.StatusCode);
        Assert.Contains("photos can only be removed while it is in progress", await DetailAsync(afterComplete));
        Assert.Equal(HttpStatusCode.Conflict, afterApprove.StatusCode);
        Assert.Contains("locked", await DetailAsync(afterApprove)); // the central lock guard (AC-08)
        Assert.Equal(PhotoPng, await manager.GetByteArrayAsync($"/api/work-orders/{job.Id}/photos/{photoId}"));
    }

    [Fact]
    public async Task Photos_are_not_required_to_complete_a_job()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technician, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);

        var completed = await CreateCompletedWorkOrderAsync(admin, manager, technician, user.Id);

        Assert.Equal("Completed", completed.Status);
        Assert.Empty(completed.Photos);
    }

    [Fact]
    public async Task A_photo_is_only_reachable_through_its_own_job()
    {
        var (admin, manager, technician, user, job) = await InProgressJobAsync();
        var otherJob = await CreateInProgressWorkOrderAsync(admin, manager, technician, user.Id);
        var photoId = (await UploadPhotoAsync(technician, job.Id)).Photos.Single().Id;

        var download = await manager.GetAsync($"/api/work-orders/{otherJob.Id}/photos/{photoId}");
        var remove = await DeletePhotoAsync(technician, otherJob.Id, photoId);

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Contains("not on this job", await DetailAsync(remove));
        Assert.Single((await GetWorkOrderAsync(technician, job.Id))!.Photos);
    }

    // ---- Ownership, state and conflicts ----

    [Fact]
    public async Task Another_technician_can_neither_upload_see_nor_remove_someone_elses_photos()
    {
        var (_, _, owner, _, job) = await InProgressJobAsync();
        var ownerJob = await UploadPhotoAsync(owner, job.Id);
        var photoId = ownerJob.Photos.Single().Id;
        var other = await _factory.ClientWithRoleAsync(AppRoles.Technician);

        using var form = PhotoForm(PhotoJpeg, ownerJob.RowVersion);
        var upload = await other.PostAsync($"/api/work-orders/{job.Id}/photos", form);
        var download = await other.GetAsync($"/api/work-orders/{job.Id}/photos/{photoId}");
        var remove = await other.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/work-orders/{job.Id}/photos/{photoId}")
        {
            Content = JsonContent.Create(new RowVersionRequest { RowVersion = ownerJob.RowVersion })
        });

        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        var after = (await GetWorkOrderAsync(owner, job.Id))!;
        Assert.Equal(ownerJob.RowVersion, after.RowVersion);
        Assert.Single(after.Photos);
    }

    [Fact]
    public async Task Only_a_job_in_progress_takes_photos()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technician, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var assigned = await CreateAssignedWorkOrderAsync(admin, manager, user.Id);
        var inProgress = await CreateInProgressWorkOrderAsync(admin, manager, technician, user.Id);
        var pending = await MarkPendingPartsAsync(technician, inProgress.Id, (await CreatePartAsync(admin)).Id, 1);
        var completed = await CreateCompletedWorkOrderAsync(admin, manager, technician, user.Id);
        var approved = await ApproveWorkOrderAsync(manager, (await CreateReportedWorkOrderAsync(admin, manager, technician, user.Id)).Id);
        var cancelled = await CancelWorkOrderAsync(manager, (await CreateInProgressWorkOrderAsync(admin, manager, technician, user.Id)).Id);

        foreach (var (job, expected) in new[]
                 {
                     (assigned, "in progress"), (pending, "in progress"), (completed, "in progress"),
                     (approved, "locked"), (cancelled, "locked")
                 })
        {
            var response = await PostPhotoAsync(technician, job.Id);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(expected, await DetailAsync(response));
            Assert.Empty((await GetWorkOrderAsync(technician, job.Id))!.Photos);
        }
    }

    [Fact]
    public async Task A_stale_row_version_is_a_409_and_the_uploaded_file_is_cleaned_up()
    {
        var (_, _, _, user, job) = await InProgressJobAsync();
        var storage = new InMemoryFileStorage();
        await using var host = _factory.WithFileStorage(storage);
        var technician = await host.SignInAsync(user);
        var stale = job.RowVersion;
        await SaveDiagnosisAsync(technician, job.Id, "Changed diagnosis", "Changed work"); // moves the token on

        using var form = PhotoForm(PhotoJpeg, stale);
        var response = await technician.PostAsync($"/api/work-orders/{job.Id}/photos", form);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("changed by someone else", await DetailAsync(response));
        Assert.Empty(storage.Keys); // the file uploaded ahead of the lost claim was deleted again
        Assert.Empty(await StoredKeysAsync(job.Id));
    }

    [Fact]
    public async Task A_storage_outage_fails_the_upload_and_leaves_the_job_untouched()
    {
        var (_, _, _, user, job) = await InProgressJobAsync();
        var storage = new InMemoryFileStorage { FailSaves = true };
        await using var host = _factory.WithFileStorage(storage);
        var technician = await host.SignInAsync(user);

        var response = await PostPhotoAsync(technician, job.Id);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.DoesNotContain("Simulated", problem!.Detail ?? string.Empty); // no internals leak to the client
        await AssertNoPhotosAsync(technician, job);
    }

    [Fact]
    public async Task Two_uploads_on_the_same_screen_have_exactly_one_winner_and_one_stored_file()
    {
        var (_, _, _, user, job) = await InProgressJobAsync();
        var storage = new InMemoryFileStorage();
        await using var host = _factory.WithFileStorage(storage);
        var technician = await host.SignInAsync(user);

        // Same rowVersion twice: a double tap on "Upload".
        using var first = PhotoForm(PhotoJpeg, job.RowVersion);
        using var second = PhotoForm(PhotoJpeg, job.RowVersion);
        var responses = await Task.WhenAll(
            technician.PostAsync($"/api/work-orders/{job.Id}/photos", first),
            technician.PostAsync($"/api/work-orders/{job.Id}/photos", second));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var key = Assert.Single(storage.Keys);
        Assert.Equal(key, Assert.Single(await StoredKeysAsync(job.Id)));
    }

    private static async Task AssertNoPhotosAsync(HttpClient technician, WorkOrderDto before)
    {
        var after = (await GetWorkOrderAsync(technician, before.Id))!;
        Assert.Empty(after.Photos);
        Assert.Equal(before.RowVersion, after.RowVersion);
    }
}
