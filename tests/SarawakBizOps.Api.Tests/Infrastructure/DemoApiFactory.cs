using Microsoft.AspNetCore.Hosting;

namespace SarawakBizOps.Api.Tests.Infrastructure;

/// <summary>
/// Boots the API the way docker-compose does: an EMPTY database, Database:MigrateOnStartup on,
/// and demo seeding on. It has its own SQL Server container (used through IClassFixture, not the
/// shared "Api" collection) so the demo rows never leak into the other tests' data.
/// </summary>
public class DemoApiFactory : ApiFactory
{
    public const string DemoPassword = "Demo-Passw0rd";

    protected override bool MigrateBeforeStartup => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Seed:Demo", "true");
        builder.UseSetting("Seed:DemoPassword", DemoPassword);
    }
}
