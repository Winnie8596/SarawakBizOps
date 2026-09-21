using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
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

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        // Program seeds roles/admin on startup and never migrates, so the
        // schema must exist before the host is built.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_sql.GetConnectionString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _sql.GetConnectionString());
        builder.UseSetting("Jwt:Key", "test-only-signing-key-that-is-long-enough-for-hmac-sha256");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _sql.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
