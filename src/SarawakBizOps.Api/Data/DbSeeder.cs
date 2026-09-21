using Microsoft.AspNetCore.Identity;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Data;

/// <summary>
/// The always-on seed: the five fixed roles and one Admin account, so there is a way to
/// log in before any real users exist (design doc Section 23). Sample business data lives
/// in <see cref="DemoSeeder"/> and is opt-in.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = services.GetRequiredService<IConfiguration>();

        // Resolve first so a missing password fails startup before anything is written.
        var adminPassword = ResolveAdminPassword(configuration);
        var adminEmail = configuration["Seed:AdminEmail"] ?? "admin@sarawakbizops.local";

        foreach (var roleName in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        if (await userManager.FindByEmailAsync(adminEmail) is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FullName = "System Administrator",
            EmailConfirmed = true,
            IsActive = true
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create the seed admin account: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(admin, AppRoles.Admin);
    }

    /// <summary>
    /// There is deliberately no built-in fallback password: a forgotten setting must stop the API
    /// from starting rather than ship a publicly known admin login. Development supplies its own
    /// value in appsettings.Development.json; demo mode reuses the demo password.
    /// </summary>
    public static string ResolveAdminPassword(IConfiguration configuration)
    {
        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password) && DemoSeeder.IsEnabled(configuration))
        {
            password = configuration["Seed:DemoPassword"];
        }

        return string.IsNullOrWhiteSpace(password)
            ? throw new InvalidOperationException(
                "Seed:AdminPassword is not configured. Set it via 'dotnet user-secrets' or the " +
                "Seed__AdminPassword environment variable (or enable demo mode with Seed:Demo=true).")
            : password;
    }
}
