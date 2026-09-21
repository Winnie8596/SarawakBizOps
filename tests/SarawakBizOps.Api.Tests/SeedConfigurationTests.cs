using Microsoft.Extensions.Configuration;
using SarawakBizOps.Api.Data;

namespace SarawakBizOps.Api.Tests;

/// <summary>The always-on seeder must never fall back to a built-in admin password.</summary>
public class SeedConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Missing_admin_password_fails_instead_of_using_a_default()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DbSeeder.ResolveAdminPassword(Config()));

        Assert.Contains("Seed:AdminPassword", ex.Message);
    }

    [Fact]
    public void Blank_admin_password_is_treated_as_missing()
    {
        Assert.Throws<InvalidOperationException>(
            () => DbSeeder.ResolveAdminPassword(Config(("Seed:AdminPassword", "  "))));
    }

    [Fact]
    public void Explicit_admin_password_is_used()
    {
        var password = DbSeeder.ResolveAdminPassword(Config(("Seed:AdminPassword", "explicit-pw-1")));

        Assert.Equal("explicit-pw-1", password);
    }

    [Fact]
    public void Demo_mode_falls_back_to_the_demo_password()
    {
        var password = DbSeeder.ResolveAdminPassword(
            Config(("Seed:Demo", "true"), ("Seed:DemoPassword", "demo-pw-1")));

        Assert.Equal("demo-pw-1", password);
    }

    [Fact]
    public void Demo_password_is_ignored_when_demo_mode_is_off()
    {
        Assert.Throws<InvalidOperationException>(
            () => DbSeeder.ResolveAdminPassword(Config(("Seed:DemoPassword", "demo-pw-1"))));
    }
}
