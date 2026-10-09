using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Storage;
using SarawakBizOps.Api.Services.WorkOrders;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Phase 8: the customer's signature (PRD §6.6, BR-03, AC-06) — stored through IFileStorage (Azurite here),
/// required to complete, readable only through the authorized download endpoint.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkOrderSignatureTests
{
    private readonly ApiFactory _factory;

    public WorkOrderSignatureTests(ApiFactory factory) => _factory = factory;

    private static async Task<string> DetailAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail ?? string.Empty;

    /// <summary>A job InProgress with its diagnosis saved — everything but the signature.</summary>
    private async Task<(HttpClient Admin, HttpClient Manager, HttpClient Technician, TestUser User, WorkOrderDto Job)> DiagnosedJobAsync()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technician, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var job = await CreateInProgressWorkOrderAsync(admin, manager, technician, user.Id);
        return (admin, manager, technician, user, await SaveDiagnosisAsync(technician, job.Id));
    }

    private async Task<string?> StoredKeyAsync(int workOrderId, IServiceProvider? services = null)
    {
        using var scope = (services ?? _factory.Services).CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.WorkOrders.Where(w => w.Id == workOrderId).Select(w => w.SignatureBlobKey).SingleAsync();
    }

    // ---- AC-06: completion rejected without a signature, accepted with one ----

    [Fact]
    public async Task Completing_without_a_signature_is_rejected_and_succeeds_once_the_customer_has_signed()
    {
        var (_, _, technician, _, job) = await DiagnosedJobAsync();

        var refused = await PostCompleteAsync(technician, job.Id);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("signature", await DetailAsync(refused));
        var unsigned = (await GetWorkOrderAsync(technician, job.Id))!;
        Assert.Equal("InProgress", unsigned.Status);
        Assert.False(unsigned.HasSignature);

        var signed = await UploadSignatureAsync(technician, job.Id);
        Assert.True(signed.HasSignature);
        Assert.NotNull(signed.SignedAt);
        Assert.NotEqual(unsigned.RowVersion, signed.RowVersion); // like every mutating call, it bumps the token

        var completed = await CompleteWorkOrderAsync(technician, job.Id);
        Assert.Equal("Completed", completed.Status);
        Assert.True(completed.HasSignature);
    }

    [Fact]
    public async Task The_diagnosis_is_still_asked_for_first_when_both_are_missing()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technician, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var job = await CreateInProgressWorkOrderAsync(admin, manager, technician, user.Id);
        await UploadSignatureAsync(technician, job.Id);

        var refused = await PostCompleteAsync(technician, job.Id);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("diagnosis", await DetailAsync(refused)); // signature alone is not enough (AC-05 still holds)
    }

    // ---- Storing and reading back ----

    [Fact]
    public async Task The_signature_is_stored_in_blob_storage_and_served_back_byte_for_byte_as_a_png()
    {
        var (admin, manager, technician, _, job) = await DiagnosedJobAsync();
        var staff = await _factory.ClientWithRoleAsync(AppRoles.ServiceStaff);

        var signed = await PostSignatureAsync(technician, job.Id);
        var json = await signed.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        // The DTO says *that* the customer signed — never where the file lives.
        var key = await StoredKeyAsync(job.Id);
        Assert.NotNull(key);
        Assert.DoesNotContain(key!, json);
        Assert.DoesNotContain("blob", json, StringComparison.OrdinalIgnoreCase);

        // The bytes really are in storage under that key (not in SQL).
        var storage = _factory.Services.GetRequiredService<IFileStorage>();
        Assert.NotNull(await storage.OpenReadAsync(key!, CancellationToken.None));

        foreach (var client in new[] { technician, manager, admin, staff })
        {
            var download = await client.GetAsync($"/api/work-orders/{job.Id}/signature");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
            Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));
            Assert.True(download.Headers.CacheControl!.NoStore);
            Assert.Equal(SignaturePng, await download.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task Signing_again_replaces_the_image_and_removes_the_old_file()
    {
        var (_, _, technician, _, job) = await DiagnosedJobAsync();
        await UploadSignatureAsync(technician, job.Id);
        var firstKey = await StoredKeyAsync(job.Id);
        var secondPng = DemoSignatureImage.Create("the customer signed again");

        await UploadSignatureAsync(technician, job.Id, secondPng);

        var secondKey = await StoredKeyAsync(job.Id);
        Assert.NotEqual(firstKey, secondKey);
        var storage = _factory.Services.GetRequiredService<IFileStorage>();
        Assert.Null(await storage.OpenReadAsync(firstKey!, CancellationToken.None));
        Assert.Equal(secondPng, await technician.GetByteArrayAsync($"/api/work-orders/{job.Id}/signature"));
    }

    [Fact]
    public async Task A_job_without_a_signature_has_nothing_to_download()
    {
        var (_, manager, _, _, job) = await DiagnosedJobAsync();

        var response = await manager.GetAsync($"/api/work-orders/{job.Id}/signature");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("no customer signature", await DetailAsync(response));
    }

    // ---- File type and size limits ----

    public static TheoryData<string, byte[], string, string> BadFiles() => new()
    {
        { "JPEG bytes declared as PNG", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0 }, "signature.png", "image/png" },
        { "SVG with a script", "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray(), "signature.png", "image/png" },
        { "plain text", "not an image"u8.ToArray(), "notes.txt", "text/plain" }
    };

    [Theory]
    [MemberData(nameof(BadFiles))]
    public async Task Anything_but_a_real_png_is_refused_whatever_it_claims_to_be(
        string _, byte[] bytes, string fileName, string contentType)
    {
        var (_, _, technician, _, job) = await DiagnosedJobAsync();

        var response = await PostSignatureAsync(technician, job.Id, bytes, fileName, contentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The signature must be a PNG image.", await DetailAsync(response));
        await AssertUnchangedAsync(technician, job);
    }

    [Fact]
    public async Task A_real_png_is_accepted_even_when_the_browser_labels_it_oddly()
    {
        var (_, _, technician, _, job) = await DiagnosedJobAsync();

        var response = await PostSignatureAsync(technician, job.Id, SignaturePng, "blob", "application/octet-stream");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused()
    {
        var (_, _, technician, _, job) = await DiagnosedJobAsync();
        var tooBig = new byte[SignatureRules.MaxBytes + 1];
        SignaturePng.AsSpan(0, 64).CopyTo(tooBig);

        var response = await PostSignatureAsync(technician, job.Id, tooBig);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("too large", await DetailAsync(response));
        await AssertUnchangedAsync(technician, job);
    }

    [Fact]
    public async Task A_request_far_over_the_limit_is_cut_off_before_it_is_buffered()
    {
        var (_, _, technician, _, job) = await DiagnosedJobAsync();

        var response = await PostSignatureAsync(technician, job.Id, new byte[3 * 1024 * 1024]);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            $"Expected 400 or 413, got {(int)response.StatusCode}");
        await AssertUnchangedAsync(technician, job);
    }

    [Fact]
    public async Task A_form_without_a_file_is_refused()
    {
        var (_, _, technician, _, job) = await DiagnosedJobAsync();

        using var form = SignatureForm(null, job.RowVersion);
        var response = await technician.PostAsync($"/api/work-orders/{job.Id}/signature", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("empty", await DetailAsync(response));
        await AssertUnchangedAsync(technician, job);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    public async Task A_missing_or_malformed_row_version_is_a_400(string rowVersion)
    {
        var (_, _, technician, _, job) = await DiagnosedJobAsync();

        using var form = SignatureForm(SignaturePng, rowVersion);
        var response = await technician.PostAsync($"/api/work-orders/{job.Id}/signature", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(technician, job);
    }

    // ---- Ownership, state and conflicts ----

    [Fact]
    public async Task Another_technician_can_neither_sign_nor_see_someone_elses_signature()
    {
        var (_, _, owner, _, job) = await DiagnosedJobAsync();
        await UploadSignatureAsync(owner, job.Id);
        var other = await _factory.ClientWithRoleAsync(AppRoles.Technician);
        var ownerJob = (await GetWorkOrderAsync(owner, job.Id))!;

        using var form = SignatureForm(SignaturePng, ownerJob.RowVersion);
        var upload = await other.PostAsync($"/api/work-orders/{job.Id}/signature", form);
        var download = await other.GetAsync($"/api/work-orders/{job.Id}/signature");

        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal(ownerJob.RowVersion, (await GetWorkOrderAsync(owner, job.Id))!.RowVersion);
    }

    [Fact]
    public async Task Only_a_job_in_progress_takes_a_signature()
    {
        var admin = await _factory.AdminClientAsync();
        var manager = await _factory.ClientWithRoleAsync(AppRoles.Manager);
        var (technician, user) = await _factory.ClientAndUserWithRoleAsync(AppRoles.Technician);
        var assigned = await CreateAssignedWorkOrderAsync(admin, manager, user.Id);
        var completed = await CreateCompletedWorkOrderAsync(admin, manager, technician, user.Id);
        var approved = await ApproveWorkOrderAsync(manager, (await CreateReportedWorkOrderAsync(admin, manager, technician, user.Id)).Id);
        var cancelled = await CancelWorkOrderAsync(manager, (await CreateInProgressWorkOrderAsync(admin, manager, technician, user.Id)).Id);

        var onAssigned = await PostSignatureAsync(technician, assigned.Id);
        var onCompleted = await PostSignatureAsync(technician, completed.Id);
        var onApproved = await PostSignatureAsync(technician, approved.Id);
        var onCancelled = await PostSignatureAsync(technician, cancelled.Id);

        Assert.Equal(HttpStatusCode.Conflict, onAssigned.StatusCode);
        Assert.Contains("in progress", await DetailAsync(onAssigned));
        Assert.Equal(HttpStatusCode.Conflict, onCompleted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, onApproved.StatusCode);
        Assert.Contains("locked", await DetailAsync(onApproved)); // the central lock guard (AC-08)
        Assert.Equal(HttpStatusCode.Conflict, onCancelled.StatusCode);
        Assert.Contains("locked", await DetailAsync(onCancelled));
    }

    [Fact]
    public async Task A_stale_row_version_is_a_409_and_the_uploaded_file_is_cleaned_up()
    {
        var (_, _, _, user, job) = await DiagnosedJobAsync();
        var storage = new InMemoryFileStorage();
        await using var host = HostWith(storage);
        var technician = await SignInAsync(host, user);
        var stale = job.RowVersion;
        await SaveDiagnosisAsync(technician, job.Id, "Changed diagnosis", "Changed work"); // moves the token on

        using var form = SignatureForm(SignaturePng, stale);
        var response = await technician.PostAsync($"/api/work-orders/{job.Id}/signature", form);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("changed by someone else", await DetailAsync(response));
        Assert.Empty(storage.Keys); // the file uploaded ahead of the lost UPDATE was deleted again
        Assert.Null(await StoredKeyAsync(job.Id));
    }

    [Fact]
    public async Task A_storage_outage_fails_the_upload_and_leaves_the_job_untouched()
    {
        var (_, _, _, user, job) = await DiagnosedJobAsync();
        var storage = new InMemoryFileStorage { FailSaves = true };
        await using var host = HostWith(storage);
        var technician = await SignInAsync(host, user);

        var response = await PostSignatureAsync(technician, job.Id);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.DoesNotContain("Simulated", problem!.Detail ?? string.Empty); // no internals leak to the client
        await AssertUnchangedAsync(technician, job);
    }

    [Fact]
    public async Task Two_signature_uploads_on_the_same_screen_have_exactly_one_winner_and_one_stored_file()
    {
        var (_, _, _, user, job) = await DiagnosedJobAsync();
        var storage = new InMemoryFileStorage();
        await using var host = HostWith(storage);
        var technician = await SignInAsync(host, user);

        // Same rowVersion twice: a double tap on "Save signature".
        using var first = SignatureForm(SignaturePng, job.RowVersion);
        using var second = SignatureForm(SignaturePng, job.RowVersion);
        var responses = await Task.WhenAll(
            technician.PostAsync($"/api/work-orders/{job.Id}/signature", first),
            technician.PostAsync($"/api/work-orders/{job.Id}/signature", second));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var key = Assert.Single(storage.Keys);
        Assert.Equal(key, await StoredKeyAsync(job.Id));
    }

    private static async Task AssertUnchangedAsync(HttpClient technician, WorkOrderDto before)
    {
        var after = (await GetWorkOrderAsync(technician, before.Id))!;
        Assert.False(after.HasSignature);
        Assert.Null(after.SignedAt);
        Assert.Equal(before.RowVersion, after.RowVersion);
    }

    private WebApplicationFactory<Program> HostWith(InMemoryFileStorage storage) => _factory.WithFileStorage(storage);

    private static Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, TestUser user) => factory.SignInAsync(user);
}
