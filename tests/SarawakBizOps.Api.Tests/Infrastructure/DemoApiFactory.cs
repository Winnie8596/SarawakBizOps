using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;

namespace SarawakBizOps.Api.Tests.Infrastructure;

/// <summary>
/// Boots the API the way docker-compose does: an EMPTY database, Database:MigrateOnStartup on,
/// and demo seeding on. It has its own SQL Server container (used through IClassFixture, not the
/// shared "Api" collection) so the demo rows never leak into the other tests' data.
/// Like compose, sa only runs scripts/sql/create-logins.sql; the API itself connects as the least-privilege
/// app login, and the AI features as the SELECT-only login.
/// </summary>
public class DemoApiFactory : ApiFactory
{
    public const string DemoPassword = "Demo-Passw0rd";

    public const string DatabaseName = "SarawakBizOpsDb";
    public const string AppLogin = "sarawakbizops_app";
    public const string AiLogin = "sarawakbizops_ai_reader";
    private const string AppPassword = "Test_App_Passw0rd!";
    private const string AiPassword = "Test_AiRead_Passw0rd!";

    protected override bool MigrateBeforeStartup => false;

    public string AppConnectionString => ConnectionStringFor(AppLogin, AppPassword);
    public string AiConnectionString => ConnectionStringFor(AiLogin, AiPassword);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // The same file compose's db-init service runs, through the container's own sqlcmd.
        var script = string.Join('\n',
            $":setvar DatabaseName {DatabaseName}",
            $":setvar AppLogin {AppLogin}",
            $":setvar AppPassword {AppPassword}",
            $":setvar AiLogin {AiLogin}",
            $":setvar AiPassword {AiPassword}",
            await File.ReadAllTextAsync(CreateLoginsScriptPath));
        var result = await Sql.ExecScriptAsync(script);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"create-logins.sql failed ({result.ExitCode}): {result.Stdout}{result.Stderr}");
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("ConnectionStrings:DefaultConnection", AppConnectionString);
        builder.UseSetting("ConnectionStrings:AiReadOnly", AiConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Seed:Demo", "true");
        builder.UseSetting("Seed:DemoPassword", DemoPassword);
    }

    private string ConnectionStringFor(string login, string password) =>
        new SqlConnectionStringBuilder(Sql.GetConnectionString())
        {
            InitialCatalog = DatabaseName,
            UserID = login,
            Password = password
        }.ConnectionString;

    private static string CreateLoginsScriptPath
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "scripts", "sql", "create-logins.sql");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            throw new FileNotFoundException("scripts/sql/create-logins.sql not found above the test output folder.");
        }
    }
}
