using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Data;

/// <summary>
/// Opt-in sample data for demos (Seed:Demo=true): one account per role plus fictional customers,
/// equipment and service requests. Every row is inserted only if its natural key is missing (email,
/// company name, serial number, equipment + problem text), so it is safe to run on every start and a
/// half-finished earlier run heals itself.
/// Later phases extend this class with their lifecycle states.
/// All names, numbers and addresses are fictional.
/// </summary>
public static class DemoSeeder
{
    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue<bool>("Seed:Demo");

    // The Admin demo account is the always-on admin from DbSeeder (admin@sarawakbizops.local).
    public static readonly IReadOnlyList<DemoUser> Users = new[]
    {
        new DemoUser("manager@sarawakbizops.local", "Jenny Lau", AppRoles.Manager),
        new DemoUser("staff@sarawakbizops.local", "Aina Abdullah", AppRoles.ServiceStaff),
        new DemoUser("technician@sarawakbizops.local", "Ahmad Bujang", AppRoles.Technician),
        new DemoUser("warehouse@sarawakbizops.local", "Siti Nurul", AppRoles.WarehouseStaff)
    };

    public record DemoUser(string Email, string FullName, string Role);

    private record DemoCustomer(string CompanyName, string ContactPerson, string Phone, string Email, string Address);

    private record DemoEquipment(
        string CustomerName, string SerialNumber, string Type, string Brand, string Model,
        DateTime InstallationDate, EquipmentStatus Status, string Location);

    private static readonly DemoCustomer[] Customers =
    {
        new("Kuching Cold Chain Sdn Bhd", "Lim Wei Jie", "+60 82 555 0101", "ops@kuchingcoldchain.example",
            "Lot 214, Jalan Demak Laut, 93050 Kuching, Sarawak"),
        new("Santubong Bottling Works Sdn Bhd", "Nurul Huda", "+60 82 555 0102", "plant@santubongbottling.example",
            "Lot 88, Kuching Industrial Estate, 93450 Kuching, Sarawak"),
        new("Rajang Timber Mills Sdn Bhd", "Tiong Kah Hui", "+60 84 555 0103", "maintenance@rajangtimber.example",
            "KM 7, Jalan Ulu Oya, 96000 Sibu, Sarawak"),
        new("Sibu Palm Oil Processing Sdn Bhd", "Rosli Ibrahim", "+60 84 555 0104", "mill@sibupalmoil.example",
            "Lot 1021, Jalan Salim, 96000 Sibu, Sarawak"),
        new("Miri Offshore Supplies Sdn Bhd", "Grace Anak Jimbun", "+60 85 555 0105", "yard@mirioffshore.example",
            "Lot 305, Jalan Miri-Bintulu, 98000 Miri, Sarawak")
    };

    private static readonly DemoEquipment[] Equipment =
    {
        new("Kuching Cold Chain Sdn Bhd", "KCC-CHL-001", "Industrial Chiller", "Norvik", "NC-450",
            new DateTime(2022, 3, 14), EquipmentStatus.Active, "Cold room 1"),
        new("Kuching Cold Chain Sdn Bhd", "KCC-CMP-002", "Air Compressor", "Hartmann", "HC-75",
            new DateTime(2021, 8, 2), EquipmentStatus.Active, "Plant room"),
        new("Santubong Bottling Works Sdn Bhd", "SBW-FIL-001", "Bottle Filling Line", "Tanjung", "TF-12",
            new DateTime(2020, 11, 20), EquipmentStatus.UnderMaintenance, "Line A"),
        new("Santubong Bottling Works Sdn Bhd", "SBW-GEN-002", "Standby Generator", "Kestrel", "KG-250",
            new DateTime(2019, 6, 5), EquipmentStatus.Active, "Generator house"),
        new("Rajang Timber Mills Sdn Bhd", "RTM-BSW-001", "Band Saw", "Hartmann", "HB-900",
            new DateTime(2018, 1, 30), EquipmentStatus.Active, "Sawmill floor"),
        new("Rajang Timber Mills Sdn Bhd", "RTM-DRY-002", "Timber Drying Kiln", "Norvik", "NK-30",
            new DateTime(2020, 9, 17), EquipmentStatus.Active, "Kiln bay 2"),
        new("Rajang Timber Mills Sdn Bhd", "RTM-CNV-003", "Log Conveyor", "Tanjung", "TC-40",
            new DateTime(2017, 4, 12), EquipmentStatus.Inactive, "Log yard"),
        new("Sibu Palm Oil Processing Sdn Bhd", "SPO-BLR-001", "Steam Boiler", "Kestrel", "KB-10T",
            new DateTime(2016, 10, 8), EquipmentStatus.Active, "Boiler house"),
        new("Sibu Palm Oil Processing Sdn Bhd", "SPO-PMP-002", "Centrifugal Pump", "Hartmann", "HP-150",
            new DateTime(2021, 2, 23), EquipmentStatus.Active, "Clarification station"),
        new("Miri Offshore Supplies Sdn Bhd", "MOS-FLT-001", "Forklift", "Tanjung", "TL-35D",
            new DateTime(2022, 7, 11), EquipmentStatus.Active, "Warehouse yard")
    };

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var password = configuration["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Seed:Demo is enabled but Seed:DemoPassword is not set. Demo accounts need a documented, demo-only password.");
        }

        await SeedUsersAsync(services, password);

        var db = services.GetRequiredService<ApplicationDbContext>();
        await SeedCustomersAndEquipmentAsync(db, ct);
        await SeedServiceRequestsAsync(services.GetRequiredService<UserManager<ApplicationUser>>(), db, ct);
    }

    // Phase 3 states only: New, Approved and Rejected. Assigned and Cancelled rows arrive with the
    // phases that can produce them, so the demo never shows a state the app cannot reach yet.
    private record DemoRequest(
        string EquipmentSerial, string Problem, RequestPriority Priority, ServiceRequestStatus Status,
        int CreatedDaysAgo, int? ReviewedDaysAgo = null, string? RejectionReason = null);

    private static readonly DemoRequest[] Requests =
    {
        new("KCC-CHL-001",
            "Chiller in cold room 1 is not holding temperature; the compressor cycles every few minutes.",
            RequestPriority.High, ServiceRequestStatus.New, CreatedDaysAgo: 1),
        new("SBW-FIL-001",
            "Filling line A is rejecting about 1 in 10 bottles; fill levels are inconsistent.",
            RequestPriority.Medium, ServiceRequestStatus.New, CreatedDaysAgo: 2),
        new("SPO-BLR-001",
            "Steam boiler pressure gauge reads erratically and the safety valve vented twice overnight.",
            RequestPriority.Urgent, ServiceRequestStatus.New, CreatedDaysAgo: 3),
        new("RTM-BSW-001",
            "Band saw blade tracking drifts after warm-up, with unusual vibration on the sawmill floor.",
            RequestPriority.Medium, ServiceRequestStatus.Approved, CreatedDaysAgo: 9, ReviewedDaysAgo: 8),
        new("SPO-PMP-002",
            "Centrifugal pump seal is leaking at the clarification station.",
            RequestPriority.High, ServiceRequestStatus.Approved, CreatedDaysAgo: 14, ReviewedDaysAgo: 13),
        new("RTM-CNV-003",
            "Log conveyor motor trips out under load.",
            RequestPriority.Low, ServiceRequestStatus.Rejected, CreatedDaysAgo: 20, ReviewedDaysAgo: 19,
            RejectionReason: "The conveyor is inactive pending replacement, so a repair is not worthwhile. " +
                             "Raise a new request if the unit is reinstated.")
    };

    // Natural key: equipment serial number + problem text. Created by the demo ServiceStaff account and
    // reviewed by the demo Manager, with dates spread over three weeks so lists and history look lived-in.
    private static async Task SeedServiceRequestsAsync(
        UserManager<ApplicationUser> userManager, ApplicationDbContext db, CancellationToken ct)
    {
        var staff = await userManager.FindByEmailAsync("staff@sarawakbizops.local")
            ?? throw new InvalidOperationException("Demo ServiceStaff account is missing.");
        var manager = await userManager.FindByEmailAsync("manager@sarawakbizops.local")
            ?? throw new InvalidOperationException("Demo Manager account is missing.");

        var equipmentBySerial = await db.Equipment.ToDictionaryAsync(e => e.SerialNumber, StringComparer.OrdinalIgnoreCase, ct);
        var existing = (await db.ServiceRequests.Select(r => new { r.EquipmentId, r.ProblemDescription }).ToListAsync(ct))
            .Select(r => (r.EquipmentId, r.ProblemDescription))
            .ToHashSet();

        var now = DateTime.UtcNow;
        foreach (var demo in Requests)
        {
            var equipment = equipmentBySerial[demo.EquipmentSerial];
            if (!existing.Add((equipment.Id, demo.Problem)))
            {
                continue;
            }

            var request = new ServiceRequest
            {
                CustomerId = equipment.CustomerId,
                EquipmentId = equipment.Id,
                ProblemDescription = demo.Problem,
                Priority = demo.Priority,
                Status = demo.Status,
                CreatedByUserId = staff.Id,
                CreatedAt = now.AddDays(-demo.CreatedDaysAgo)
            };

            if (demo.Status == ServiceRequestStatus.Approved)
            {
                request.ApprovedByUserId = manager.Id;
                request.ApprovedAt = now.AddDays(-demo.ReviewedDaysAgo!.Value);
            }
            else if (demo.Status == ServiceRequestStatus.Rejected)
            {
                request.RejectedByUserId = manager.Id;
                request.RejectedAt = now.AddDays(-demo.ReviewedDaysAgo!.Value);
                request.RejectionReason = demo.RejectionReason;
            }

            db.ServiceRequests.Add(request);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedUsersAsync(IServiceProvider services, string password)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var demo in Users)
        {
            if (await userManager.FindByEmailAsync(demo.Email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = demo.Email,
                Email = demo.Email,
                FullName = demo.FullName,
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create demo user {demo.Email}: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(user, demo.Role);
        }
    }

    private static async Task SeedCustomersAndEquipmentAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var existingCustomers = await db.Customers.ToDictionaryAsync(c => c.CompanyName, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var demo in Customers)
        {
            if (existingCustomers.ContainsKey(demo.CompanyName))
            {
                continue;
            }

            var customer = new Customer
            {
                CompanyName = demo.CompanyName,
                ContactPerson = demo.ContactPerson,
                Phone = demo.Phone,
                Email = demo.Email,
                Address = demo.Address
            };
            db.Customers.Add(customer);
            existingCustomers[demo.CompanyName] = customer;
        }

        // Save customers first so their generated ids exist for the equipment below.
        await db.SaveChangesAsync(ct);

        var existingSerials = (await db.Equipment.Select(e => e.SerialNumber).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var demo in Equipment)
        {
            if (!existingSerials.Add(demo.SerialNumber))
            {
                continue;
            }

            db.Equipment.Add(new Equipment
            {
                CustomerId = existingCustomers[demo.CustomerName].Id,
                SerialNumber = demo.SerialNumber,
                EquipmentType = demo.Type,
                Brand = demo.Brand,
                Model = demo.Model,
                InstallationDate = demo.InstallationDate,
                Status = demo.Status,
                Location = demo.Location
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
