using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.Services.Storage;

namespace SarawakBizOps.Api.Tests.Infrastructure;

/// <summary>
/// A second host on the same database whose <see cref="IFileStorage"/> is the in-memory fake, so a test can
/// see (or break) exactly what reaches storage — no production seam needed (Phases 8 and 9).
/// </summary>
public static class FakeStorageHost
{
    public static WebApplicationFactory<Program> WithFileStorage(this ApiFactory factory, InMemoryFileStorage storage)
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage>(storage);
        }));

    public static async Task<HttpClient> SignInAsync(this WebApplicationFactory<Program> factory, TestUser user)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email, Password = user.Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
