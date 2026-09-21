using System.Net;
using System.Net.Http.Json;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.DTOs.Users;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

/// <summary>Phase 1: Identity and user administration (PRD §6.1, BR-08, AC-11 groundwork).</summary>
[Collection(ApiCollection.Name)]
public class UserManagementTests
{
    private readonly ApiFactory _factory;

    public UserManagementTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> AdminClientAsync()
        => _factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

    private Task<HttpClient> ClientForAsync(TestUser user)
        => _factory.CreateAuthenticatedClientAsync(user.Email, user.Password);

    private async Task<HttpResponseMessage> LoginAsync(string email, string password)
        => await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = password });

    // ---- Exit criterion: Admin can create one user per role and each can log in ----

    [Theory]
    [InlineData(AppRoles.Admin)]
    [InlineData(AppRoles.Manager)]
    [InlineData(AppRoles.ServiceStaff)]
    [InlineData(AppRoles.Technician)]
    [InlineData(AppRoles.WarehouseStaff)]
    public async Task Admin_creates_a_user_for_each_role_and_that_user_can_log_in(string role)
    {
        var admin = await AdminClientAsync();

        var user = await TestUsers.CreateAsync(admin, role);
        var login = await LoginAsync(user.Email, user.Password);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(new[] { role }, body!.Roles);
    }

    // ---- Authorization: only Admin may use /api/users (BR-08, AC-11) ----

    [Theory]
    [InlineData(AppRoles.Manager)]
    [InlineData(AppRoles.ServiceStaff)]
    [InlineData(AppRoles.Technician)]
    [InlineData(AppRoles.WarehouseStaff)]
    public async Task Non_admin_roles_get_403_on_every_user_endpoint(string role)
    {
        var admin = await AdminClientAsync();
        var target = await TestUsers.CreateAsync(admin, AppRoles.Technician);
        var client = await ClientForAsync(await TestUsers.CreateAsync(admin, role));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/users/{target.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            FullName = "x", Email = $"{Guid.NewGuid():N}@test.local", Password = "Passw0rd-test", Role = AppRoles.Manager
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/users/{target.Id}",
            new UpdateUserRequest { IsActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/users/{target.Id}/reset-password",
            new ResetPasswordRequest { NewPassword = "Another-pass1" })).StatusCode);
    }

    [Fact]
    public async Task Anonymous_caller_gets_401_on_users()
    {
        var response = await _factory.CreateClient().GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_returns_each_users_single_role()
    {
        var admin = await AdminClientAsync();
        var tech = await TestUsers.CreateAsync(admin, AppRoles.Technician);

        var users = await admin.GetFromJsonAsync<List<UserDto>>("/api/users");

        var listed = Assert.Single(users!, u => u.Id == tech.Id);
        Assert.Equal(AppRoles.Technician, listed.Role);
        Assert.True(listed.IsActive);
    }

    // ---- Create validation ----

    [Fact]
    public async Task Create_rejects_duplicate_email()
    {
        var admin = await AdminClientAsync();
        var existing = await TestUsers.CreateAsync(admin, AppRoles.Manager);

        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            FullName = "Dup", Email = existing.Email, Password = "Passw0rd-test", Role = AppRoles.Manager
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_unknown_role_and_weak_password()
    {
        var admin = await AdminClientAsync();

        var badRole = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            FullName = "x", Email = $"{Guid.NewGuid():N}@test.local", Password = "Passw0rd-test", Role = "Superuser"
        });
        var shortPassword = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            FullName = "x", Email = $"{Guid.NewGuid():N}@test.local", Password = "short", Role = AppRoles.Manager
        });

        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
    }

    // ---- Deactivation ----

    [Fact]
    public async Task Deactivated_user_cannot_log_in()
    {
        var admin = await AdminClientAsync();
        var user = await TestUsers.CreateAsync(admin, AppRoles.Technician);

        var patch = await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest { IsActive = false });

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(user.Email, user.Password)).StatusCode);
    }

    [Fact]
    public async Task Deactivating_a_user_revokes_a_token_they_already_hold()
    {
        var admin = await AdminClientAsync();
        var user = await TestUsers.CreateAsync(admin, AppRoles.Technician);
        var userClient = await ClientForAsync(user);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/auth/me")).StatusCode);

        await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest { IsActive = false });

        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Reactivated_user_can_log_in_again()
    {
        var admin = await AdminClientAsync();
        var user = await TestUsers.CreateAsync(admin, AppRoles.Technician);
        await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest { IsActive = false });

        await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest { IsActive = true });

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email, user.Password)).StatusCode);
    }

    // ---- Role changes ----

    [Fact]
    public async Task Changing_a_role_replaces_it_and_revokes_the_old_token()
    {
        var admin = await AdminClientAsync();
        var user = await TestUsers.CreateAsync(admin, AppRoles.Manager);
        var oldClient = await ClientForAsync(user);

        var patch = await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest { Role = AppRoles.Technician });

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/api/auth/me")).StatusCode);

        var relogin = await LoginAsync(user.Email, user.Password);
        var body = await relogin.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(new[] { AppRoles.Technician }, body!.Roles);
    }

    [Fact]
    public async Task Admin_cannot_demote_or_deactivate_themselves()
    {
        var admin = await AdminClientAsync();
        var me = await admin.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");

        var demote = await admin.PatchAsJsonAsync($"/api/users/{me!.UserId}", new UpdateUserRequest { Role = AppRoles.Manager });
        var deactivate = await admin.PatchAsJsonAsync($"/api/users/{me.UserId}", new UpdateUserRequest { IsActive = false });

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Update_of_unknown_user_returns_404()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PatchAsJsonAsync("/api/users/does-not-exist", new UpdateUserRequest { IsActive = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Change password (self-service) ----

    [Fact]
    public async Task Change_password_succeeds_then_only_the_new_password_works()
    {
        var admin = await AdminClientAsync();
        var user = await TestUsers.CreateAsync(admin, AppRoles.ServiceStaff);
        var client = await ClientForAsync(user);

        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = user.Password, NewPassword = "Brand-new-pass1" });

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(user.Email, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email, "Brand-new-pass1")).StatusCode);
        // The security stamp rotated, so the token used to make the change is no longer valid.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Change_password_with_wrong_current_password_returns_400_and_changes_nothing()
    {
        var admin = await AdminClientAsync();
        var user = await TestUsers.CreateAsync(admin, AppRoles.ServiceStaff);
        var client = await ClientForAsync(user);

        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = "not-my-password", NewPassword = "Brand-new-pass1" });

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email, user.Password)).StatusCode);
    }

    [Fact]
    public async Task Change_password_requires_authentication()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = "a", NewPassword = "Brand-new-pass1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Admin password reset (no email, so this is the recovery path) ----

    [Fact]
    public async Task Admin_reset_sets_a_new_password_and_revokes_old_tokens()
    {
        var admin = await AdminClientAsync();
        var user = await TestUsers.CreateAsync(admin, AppRoles.WarehouseStaff);
        var oldClient = await ClientForAsync(user);

        var reset = await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password",
            new ResetPasswordRequest { NewPassword = "Reset-by-admin1" });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(user.Email, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email, "Reset-by-admin1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Admin_reset_lifts_a_lockout()
    {
        var admin = await AdminClientAsync();
        var user = await TestUsers.CreateAsync(admin, AppRoles.WarehouseStaff);
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(user.Email, "wrong-password");
        }
        // Locked out now: even the right password is refused.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(user.Email, user.Password)).StatusCode);

        await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password",
            new ResetPasswordRequest { NewPassword = "Reset-by-admin1" });

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email, "Reset-by-admin1")).StatusCode);
    }
}
