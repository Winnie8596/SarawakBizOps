using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SarawakBizOps.Api.DTOs.Auth;
using Testcontainers.Azurite;
using Testcontainers.MsSql;

namespace SarawakBizOps.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real API against a throwaway SQL Server container and an Azurite blob-storage container
/// (Testcontainers), so integration tests exercise real transactions, RowVersion behaviour and real file
/// storage (Phase 8).
/// One instance is shared by every test class in the "Api" collection.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@sarawakbizops.local";
    public const string AdminPassword = "TestAdmin123!";

    protected readonly MsSqlContainer Sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    // Same pinned image as docker-compose.yml. --skipApiVersionCheck lets a newer Azure.Storage.Blobs SDK talk
    // to the emulator (Azurite otherwise refuses REST API versions it does not know yet).
    protected readonly AzuriteContainer Blob = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.36.0")
        .WithInMemoryPersistence()
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public string StorageConnectionString => Blob.GetConnectionString();

    /// <summary>The SQL Server container's connection string (its default database), for tests that need a
    /// scratch database of their own on the same server — see <see cref="ScratchDatabase"/>.</summary>
    public string SqlConnectionString => Sql.GetConnectionString();

    /// <summary>
    /// The default factory migrates the database itself before the host is built (Program does not
    /// migrate unless Database:MigrateOnStartup is on). <see cref="DemoApiFactory"/> turns this off
    /// to prove the migrate-on-startup path against an empty database.
    /// </summary>
    protected virtual bool MigrateBeforeStartup => true;

    public virtual async Task InitializeAsync()
    {
        await Task.WhenAll(Sql.StartAsync(), Blob.StartAsync());

        if (!MigrateBeforeStartup)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(Sql.GetConnectionString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", Sql.GetConnectionString());
        builder.UseSetting("Storage:ConnectionString", Blob.GetConnectionString());
        builder.UseSetting("Jwt:Key", "test-only-signing-key-that-is-long-enough-for-hmac-sha256");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        // Phase 13: the deterministic mock, never a real provider. Tests share users across many AI calls, so the
        // per-user rate limit is lifted here; TechnicianAssistantTests proves it on a host with the limit set low.
        builder.UseSetting("Ai:Provider", "Mock");
        builder.UseSetting("Ai:PermitsPerMinute", "1000");
        // Every test signs in through the real endpoint from the same (absent) address; AuthSessionTests proves the
        // per-address login limit on a host with it set low.
        builder.UseSetting("Auth:LoginPermitsPerMinute", "100000");
        // Tests read the dashboard straight after changing data; DashboardCacheTests proves the cache itself.
        builder.UseSetting("Dashboard:CacheSeconds", "0");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.WhenAll(Sql.DisposeAsync().AsTask(), Blob.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}

public static class ApiFactoryExtensions
{
    /// <summary>Logs in through the real endpoint and returns a client carrying the bearer token.</summary>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        this ApiFactory factory, string email, string password)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = password });
        login.EnsureSuccessStatusCode();

        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }
}
