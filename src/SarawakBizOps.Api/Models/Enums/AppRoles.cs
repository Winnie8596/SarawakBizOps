namespace SarawakBizOps.Api.Models.Enums;

/// <summary>
/// Role names used by ASP.NET Core Identity (IdentityRole).
/// Plain string constants are used instead of a C# enum because Identity
/// matches roles by name at runtime ([Authorize(Roles = "...")], User.IsInRole(...)),
/// and these constants are also what DbSeeder uses to create the roles on first run.
/// See design doc Section 5 (Roles and Identity).
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string ServiceStaff = "ServiceStaff";
    public const string Technician = "Technician";
    public const string WarehouseStaff = "WarehouseStaff";

    public static readonly string[] All =
    {
        Admin, Manager, ServiceStaff, Technician, WarehouseStaff
    };
}
