using System.Net.Http.Json;
using SarawakBizOps.Api.DTOs.Users;

namespace SarawakBizOps.Api.Tests.Infrastructure;

public record TestUser(string Id, string Email, string Password, string Role);

public static class TestUsers
{
    public const string DefaultPassword = "Passw0rd-test";

    /// <summary>Creates a uniquely-named user through the real Admin endpoint.</summary>
    public static async Task<TestUser> CreateAsync(HttpClient adminClient, string role, string password = DefaultPassword)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        var response = await adminClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            FullName = $"Test {role}",
            Email = email,
            Password = password,
            Role = role
        });
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<UserDto>();
        return new TestUser(dto!.Id, email, password, role);
    }
}
