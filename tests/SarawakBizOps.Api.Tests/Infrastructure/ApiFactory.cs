using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SarawakBizOps.Api.DTOs.Auth;
using Testcontainers.MsSql;

namespace SarawakBizOps.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real API against a throwaway SQL Server container (Testcontainers),
/// so integration tests exercise real transactions and RowVersion behaviour.
/// One instance is shared by every test class in the "Api" collection.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@sarawakbizops.local";
    public const string AdminPassword = "TestAdmin123!";

    protected readonly MsSqlContainer Sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>
    /// The default factory migrates the database itself before the host is built (Program does not
    /// migrate unless Database:MigrateOnStartup is on). <see cref="DemoApiFactory"/> turns this off
    /// to prove the migrate-on-startup path against an empty database.
    /// </summary>
    protected virtual bool MigrateBeforeStartup => true;

    public async Task InitializeAsync()
    {
        await Sql.StartAsync();

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
        builder.UseSetting("Jwt:Key", "test-only-signing-key-that-is-long-enough-for-hmac-sha256");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Sql.DisposeAsync();
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
