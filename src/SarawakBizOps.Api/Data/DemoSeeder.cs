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
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        await SeedCustomersAndEquipmentAsync(db, ct);
        await SeedServiceRequestsAsync(userManager, db, ct);
        await SeedWorkOrdersAsync(userManager, db, ct);
    }

    // Phase 3 states: New, Approved and Rejected. Four of the Approved rows below are then carried
    // into a WorkOrder by SeedWorkOrdersAsync (their ServiceRequestStatus becomes Assigned, or Approved
    // again for the one whose job gets cancelled); the Centrifugal Pump request is deliberately left
    // Approved-but-unassigned so a reviewer always has one request to assign by hand. Phase 5 adds two
    // Cancelled examples: one cancelled by the creator while still New (decision: ServiceStaff/Admin may
    // undo their own mistake), one cancelled by the Manager after being Approved.
    private record DemoRequest(
        string EquipmentSerial, string Problem, RequestPriority Priority, ServiceRequestStatus Status,
        int CreatedDaysAgo, int? ReviewedDaysAgo = null, string? RejectionReason = null,
        int? CancelledDaysAgo = null, string? CancellationReason = null, bool CancelledByCreator = false);

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
                             "Raise a new request if the unit is reinstated."),
        new("KCC-CMP-002",
            "Air compressor short-cycles under load and trips its overload relay.",
            RequestPriority.High, ServiceRequestStatus.Approved, CreatedDaysAgo: 8, ReviewedDaysAgo: 7),
        new("SBW-GEN-002",
            "Standby generator fails to reach rated voltage on load and the AVR indicator flashes.",
            RequestPriority.Urgent, ServiceRequestStatus.Approved, CreatedDaysAgo: 12, ReviewedDaysAgo: 11),
        new("MOS-FLT-001",
            "Forklift hydraulic lift is slow to raise a loaded pallet and leaks a little fluid at the mast.",
            RequestPriority.Medium, ServiceRequestStatus.Approved, CreatedDaysAgo: 18, ReviewedDaysAgo: 17),
        new("RTM-DRY-002",
            "Timber drying kiln temperature sensor reading looks wrong, but it may just be a loose lead.",
            RequestPriority.Low, ServiceRequestStatus.Cancelled, CreatedDaysAgo: 4,
            CancelledDaysAgo: 3, CancelledByCreator: true,
            CancellationReason: "The sensor lead was reseated and re-tested outside the system before this reached " +
                                 "review; no repair job is needed after all."),
        new("SBW-FIL-001",
            "Filling line A conveyor belt is fraying and needs replacing before it fails completely.",
            RequestPriority.Medium, ServiceRequestStatus.Approved, CreatedDaysAgo: 11, ReviewedDaysAgo: 10)
    };

    // Phase 4 states: one job at each of Assigned, InProgress, Completed and Approved, all worked by
    // the demo Technician, so every status the UI can show — and both the technician's and the
    // Manager's next action — is represented without touching the still-unassigned pump request above.
    // Phase 5 adds one Cancelled job, whose linked request reverts to Approved (BR-12's filtered unique
    // index then lets it be reassigned) — the seeded proof that the revert-and-reassign path works.
    private record DemoWorkOrder(
        string EquipmentSerial, string Problem, WorkOrderStatus Status, int AssignedDaysAgo,
        int? StartedDaysAgo = null, int? CompletedDaysAgo = null, int? ApprovedDaysAgo = null,
        string? Diagnosis = null, string? WorkPerformed = null,
        int? CancelledDaysAgo = null, string? CancellationReason = null);

    private static readonly DemoWorkOrder[] WorkOrders =
    {
        // Assigned only: the technician's next job is to tap Start.
        new("RTM-BSW-001",
            "Band saw blade tracking drifts after warm-up, with unusual vibration on the sawmill floor.",
            WorkOrderStatus.Assigned, AssignedDaysAgo: 7),

        // In progress with the evidence already recorded: ready for the technician to tap Complete.
        new("KCC-CMP-002",
            "Air compressor short-cycles under load and trips its overload relay.",
            WorkOrderStatus.InProgress, AssignedDaysAgo: 6, StartedDaysAgo: 5,
            Diagnosis: "Overload relay set point was too low for the compressor's duty cycle; contacts were also pitted.",
            WorkPerformed: "Replaced the overload relay, reset it to the compressor's rated current and ran a 30-minute load test."),

        // Completed: the Manager's next action is to approve it.
        new("SBW-GEN-002",
            "Standby generator fails to reach rated voltage on load and the AVR indicator flashes.",
            WorkOrderStatus.Completed, AssignedDaysAgo: 10, StartedDaysAgo: 10, CompletedDaysAgo: 9,
            Diagnosis: "AVR sensing lead had a loose crimp, causing voltage regulation to hunt under load.",
            WorkPerformed: "Re-terminated the AVR sensing lead, recalibrated the voltage set point and confirmed with a 3-hour load-bank test."),

        // Approved: the whole workflow closed, shown read-only.
        new("MOS-FLT-001",
            "Forklift hydraulic lift is slow to raise a loaded pallet and leaks a little fluid at the mast.",
            WorkOrderStatus.Approved, AssignedDaysAgo: 16, StartedDaysAgo: 16, CompletedDaysAgo: 15, ApprovedDaysAgo: 14,
            Diagnosis: "Hydraulic pump inlet strainer was clogged and a lift-cylinder seal was worn, causing external leakage.",
            WorkPerformed: "Cleaned the inlet strainer, replaced the lift-cylinder seal kit and topped up hydraulic fluid to spec."),

        // Cancelled before it could be started: the request behind it reverts to Approved and shows up
        // in the assign queue again, exactly as a real cancel would leave it.
        new("SBW-FIL-001",
            "Filling line A conveyor belt is fraying and needs replacing before it fails completely.",
            WorkOrderStatus.Cancelled, AssignedDaysAgo: 5,
            CancelledDaysAgo: 4,
            CancellationReason: "The parts supplier could not confirm a belt in stock; cancelled so the job can be " +
                                 "reassigned once stock arrives.")
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
            else if (demo.Status == ServiceRequestStatus.Cancelled)
            {
                // Decision: the creator-side roles may cancel their own New request; a Manager cancels
                // an already-Approved one. Both are represented across the two Cancelled demo rows.
                request.CancelledByUserId = demo.CancelledByCreator ? staff.Id : manager.Id;
                request.CancelledAt = now.AddDays(-demo.CancelledDaysAgo!.Value);
                request.CancellationReason = demo.CancellationReason;
            }

            db.ServiceRequests.Add(request);
        }

        await db.SaveChangesAsync(ct);
    }

    // Natural key: the ServiceRequestId (BR-12 — one WorkOrder per request), found via the same
    // equipment serial + problem text used above. Assigning bumps the request to Assigned, exactly as
    // the real /assign endpoint would, so the seeded rows stay consistent with the state machine.
    private static async Task SeedWorkOrdersAsync(
        UserManager<ApplicationUser> userManager, ApplicationDbContext db, CancellationToken ct)
    {
        var technician = await userManager.FindByEmailAsync("technician@sarawakbizops.local")
            ?? throw new InvalidOperationException("Demo Technician account is missing.");
        var manager = await userManager.FindByEmailAsync("manager@sarawakbizops.local")
            ?? throw new InvalidOperationException("Demo Manager account is missing.");

        // Grouped rather than ToDictionaryAsync: the key is only unique among the seeded rows
        // themselves. Real requests created through the API (e.g. a repeated demo/smoke run raising
        // the same fault on the same equipment) can share it too, and a duplicate key must not crash
        // startup. OrderBy(Id) makes "first per key" the earliest row — the original seeded one.
        var requestsByKey = (await db.ServiceRequests
                .Include(r => r.Equipment)
                .OrderBy(r => r.Id)
                .ToListAsync(ct))
            .GroupBy(r => (r.Equipment!.SerialNumber, r.ProblemDescription))
            .ToDictionary(g => g.Key, g => g.First());
        var alreadyAssigned = (await db.WorkOrders.Select(w => w.ServiceRequestId).ToListAsync(ct)).ToHashSet();

        var now = DateTime.UtcNow;
        foreach (var demo in WorkOrders)
        {
            if (!requestsByKey.TryGetValue((demo.EquipmentSerial, demo.Problem), out var request)
                || !alreadyAssigned.Add(request.Id))
            {
                continue;
            }

            var workOrder = new WorkOrder
            {
                ServiceRequestId = request.Id,
                TechnicianId = technician.Id,
                Status = demo.Status,
                AssignedAt = now.AddDays(-demo.AssignedDaysAgo),
                StartedAt = demo.StartedDaysAgo.HasValue ? now.AddDays(-demo.StartedDaysAgo.Value) : null,
                CompletedAt = demo.CompletedDaysAgo.HasValue ? now.AddDays(-demo.CompletedDaysAgo.Value) : null,
                ApprovedAt = demo.ApprovedDaysAgo.HasValue ? now.AddDays(-demo.ApprovedDaysAgo.Value) : null,
                Diagnosis = demo.Diagnosis,
                WorkPerformed = demo.WorkPerformed
            };

            if (demo.Status == WorkOrderStatus.Cancelled)
            {
                workOrder.CancelledByUserId = manager.Id;
                workOrder.CancelledAt = now.AddDays(-demo.CancelledDaysAgo!.Value);
                workOrder.CancellationReason = demo.CancellationReason;
            }

            db.WorkOrders.Add(workOrder);

            // Decision: cancelling a work order reverts its request to Approved (re-assignable, thanks
            // to BR-12's filtered unique index); every other demo status carries the request to
            // Assigned, exactly as the real /assign endpoint would.
            request.Status = demo.Status == WorkOrderStatus.Cancelled
                ? ServiceRequestStatus.Approved
                : ServiceRequestStatus.Assigned;
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
