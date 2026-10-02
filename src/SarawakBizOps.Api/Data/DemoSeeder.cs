using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Reports;
using SarawakBizOps.Api.Services.Storage;
using SarawakBizOps.Api.Services.WorkOrders;

namespace SarawakBizOps.Api.Data;

/// <summary>
/// Opt-in sample data for demos (Seed:Demo=true): one account per role plus fictional customers,
/// equipment, service requests, work orders and a parts catalogue with its stock ledger. Every row is
/// inserted only if its natural key is missing (email, company name, serial number, equipment + problem
/// text, part number), so it is safe to run on every start and a
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
            "Lot 305, Jalan Miri-Bintulu, 98000 Miri, Sarawak"),
        // Phase 15: the PRD's ~10 customers. These five have equipment but no jobs yet, so the dashboard's
        // hand-computed figures are unchanged.
        new("Batu Kawa Poultry Farm Sdn Bhd", "Chong Mei Ling", "+60 82 555 0106", "farm@batukawapoultry.example",
            "Lot 47, Jalan Batu Kawa-Matang, 93250 Kuching, Sarawak"),
        new("Pending Industrial Laundry Sdn Bhd", "Mohd Faizal Sulaiman", "+60 82 555 0107", "service@pendinglaundry.example",
            "Lot 912, Jalan Pending, 93450 Kuching, Sarawak"),
        new("Lanang Ice Factory Sdn Bhd", "Wong Siew Ping", "+60 84 555 0108", "plant@lanangice.example",
            "Lot 63, Jalan Lanang, 96000 Sibu, Sarawak"),
        new("Lutong Aquaculture Sdn Bhd", "Jeffrey Anak Ngumbang", "+60 85 555 0109", "ponds@lutongaqua.example",
            "Lot 1180, Jalan Kuala Baram, 98100 Miri, Sarawak"),
        new("Pujut Packaging Sdn Bhd", "Kong Li Hua", "+60 85 555 0110", "factory@pujutpackaging.example",
            "Lot 2215, Jalan Pujut 7, 98000 Miri, Sarawak")
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
            new DateTime(2022, 7, 11), EquipmentStatus.Active, "Warehouse yard"),
        new("Santubong Bottling Works Sdn Bhd", "SBW-FIL-003", "Bottle Filling Line", "Tanjung", "TF-12",
            new DateTime(2023, 5, 9), EquipmentStatus.UnderMaintenance, "Line B"),

        // Phase 15: the PRD's ~30 items, with no jobs (the figures above stay as they are). The retired boiler is
        // the one equipment status the earlier rows did not show.
        new("Kuching Cold Chain Sdn Bhd", "KCC-FLT-003", "Forklift", "Tanjung", "TL-25E",
            new DateTime(2023, 2, 6), EquipmentStatus.Active, "Loading dock"),
        new("Kuching Cold Chain Sdn Bhd", "KCC-GEN-004", "Standby Generator", "Kestrel", "KG-150",
            new DateTime(2020, 4, 15), EquipmentStatus.Active, "Generator house"),
        new("Santubong Bottling Works Sdn Bhd", "SBW-CMP-004", "Air Compressor", "Hartmann", "HC-55",
            new DateTime(2022, 1, 19), EquipmentStatus.Active, "Utility room"),
        new("Rajang Timber Mills Sdn Bhd", "RTM-BLR-004", "Steam Boiler", "Kestrel", "KB-5T",
            new DateTime(2009, 7, 1), EquipmentStatus.Retired, "Old boiler house (decommissioned)"),
        new("Rajang Timber Mills Sdn Bhd", "RTM-GEN-005", "Standby Generator", "Kestrel", "KG-400",
            new DateTime(2021, 10, 4), EquipmentStatus.Active, "Generator house"),
        new("Sibu Palm Oil Processing Sdn Bhd", "SPO-PRS-003", "Screw Press", "Norvik", "NSP-15",
            new DateTime(2019, 3, 27), EquipmentStatus.Active, "Pressing station"),
        new("Sibu Palm Oil Processing Sdn Bhd", "SPO-GEN-004", "Standby Generator", "Kestrel", "KG-500",
            new DateTime(2018, 8, 13), EquipmentStatus.Active, "Generator house"),
        new("Miri Offshore Supplies Sdn Bhd", "MOS-CRN-002", "Overhead Crane", "Hartmann", "HOC-10",
            new DateTime(2019, 5, 21), EquipmentStatus.Active, "Fabrication shed"),
        new("Miri Offshore Supplies Sdn Bhd", "MOS-CMP-003", "Air Compressor", "Hartmann", "HC-75",
            new DateTime(2020, 12, 2), EquipmentStatus.Active, "Fabrication shed"),
        new("Miri Offshore Supplies Sdn Bhd", "MOS-WLD-004", "Welding Generator", "Kestrel", "KW-400",
            new DateTime(2021, 6, 30), EquipmentStatus.Active, "Fabrication shed"),
        new("Batu Kawa Poultry Farm Sdn Bhd", "BKP-CHL-001", "Industrial Chiller", "Norvik", "NC-300",
            new DateTime(2021, 9, 8), EquipmentStatus.Active, "Processing hall"),
        new("Batu Kawa Poultry Farm Sdn Bhd", "BKP-GEN-002", "Standby Generator", "Kestrel", "KG-150",
            new DateTime(2019, 2, 11), EquipmentStatus.Active, "Generator house"),
        new("Pending Industrial Laundry Sdn Bhd", "PIL-BLR-001", "Steam Boiler", "Kestrel", "KB-2T",
            new DateTime(2018, 11, 5), EquipmentStatus.Active, "Boiler room"),
        new("Pending Industrial Laundry Sdn Bhd", "PIL-WSH-002", "Industrial Washer", "Tanjung", "TW-120",
            new DateTime(2022, 4, 25), EquipmentStatus.Active, "Wash floor"),
        new("Lanang Ice Factory Sdn Bhd", "LIF-CMP-001", "Refrigeration Compressor", "Norvik", "NR-90",
            new DateTime(2017, 1, 16), EquipmentStatus.Active, "Ice plant"),
        new("Lanang Ice Factory Sdn Bhd", "LIF-PMP-002", "Centrifugal Pump", "Hartmann", "HP-80",
            new DateTime(2020, 7, 7), EquipmentStatus.Active, "Brine tank"),
        new("Lutong Aquaculture Sdn Bhd", "LAQ-PMP-001", "Centrifugal Pump", "Hartmann", "HP-100",
            new DateTime(2021, 3, 3), EquipmentStatus.Active, "Pond pump house"),
        new("Lutong Aquaculture Sdn Bhd", "LAQ-AER-002", "Pond Aerator", "Tanjung", "TA-5",
            new DateTime(2023, 8, 18), EquipmentStatus.Inactive, "Pond 4 (fallow)"),
        new("Pujut Packaging Sdn Bhd", "PPK-CNV-001", "Box Conveyor", "Tanjung", "TC-20",
            new DateTime(2022, 10, 12), EquipmentStatus.Active, "Packing line")
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
        // Parts before work orders: a PendingParts job names the part it is waiting for, and jobs use parts.
        await SeedPartsAsync(userManager, db, ct);
        await SeedWorkOrdersAsync(userManager, db, ct);
        await SeedPartsUsedAsync(userManager, db, ct);
        await SeedSignaturesAsync(userManager, db, services.GetRequiredService<IFileStorage>(), ct);
        await SeedPhotosAsync(userManager, db, services.GetRequiredService<IFileStorage>(), ct);
        await SeedReportsAsync(userManager, db, services.GetRequiredService<IReportService>(), ct);
    }

    // Phase 11: four and a half months of finished jobs behind the live ones above, so the dashboard's
    // jobs-completed-per-month trend has a shape. Each is a request that was approved, assigned, worked, completed
    // (signed, BR-03) and approved (with its report), a few days apart. No parts: the parts ledger starts three
    // weeks ago, and a job issuing stock before it was ever received would break the ledger's history. The pump
    // fails twice on purpose — Phase 12's repeated-failures metric has something to find. Every CompletedDaysAgo
    // is at most 140, so all of them fall inside the dashboard's six-month window whatever today's date is.
    // Phase 12: each job has its own wait for assignment (request raised → assigned, reviewed halfway through)
    // and time on the job (started → completed; the job was started a day after assignment), so the turnaround
    // averages have something to average. The earliest request is raised about 138 days ago — still inside the window.
    // Phase 13: the air compressor tripped on overload once before, and the band saw's guides were noisy, so the AI
    // assistant on the live compressor and band-saw jobs has a genuinely related past case to point at (and, on the
    // compressor, an unrelated one to leave out).
    private record DemoHistory(
        string EquipmentSerial, string Problem, RequestPriority Priority, int CompletedDaysAgo,
        double DaysToAssign, double DaysOnJob, string Diagnosis, string WorkPerformed)
    {
        public double StartedDaysAgo => CompletedDaysAgo + DaysOnJob;
        public double AssignedDaysAgo => StartedDaysAgo + 1;
        public double CreatedDaysAgo => AssignedDaysAgo + DaysToAssign;
    }

    private static readonly DemoHistory[] History =
    {
        new("KCC-CHL-001",
            "Chiller condenser fan motor is noisy and the unit trips on high head pressure in the afternoons.",
            RequestPriority.High, 24, 0.5, 0.25,
            "Condenser fan motor bearings worn; condenser coil fouled with dust, raising head pressure.",
            "Replaced the fan motor bearings, cleaned the condenser coil and verified head pressure through a full afternoon cycle."),
        new("SPO-BLR-001",
            "Boiler feed-water pump loses prime after the overnight shutdown.",
            RequestPriority.Urgent, 38, 0.1, 0.5,
            "Foot valve on the feed-water suction line was not seating, letting the line drain overnight.",
            "Cleaned and re-seated the foot valve, replaced its spring and confirmed the pump held prime over two shutdowns."),
        new("RTM-DRY-002",
            "Timber drying kiln circulation fan vibrates badly at full speed.",
            RequestPriority.Medium, 52, 2, 1,
            "Fan impeller out of balance from resin build-up on two blades.",
            "Cleaned the impeller, balanced it on site and checked vibration was back within limits at full speed."),
        new("SPO-PMP-002",
            "Centrifugal pump bearing housing is running hot and the pump is noisier than usual.",
            RequestPriority.High, 66, 1, 0.5,
            "Bearing grease had broken down from over-temperature; coupling slightly misaligned.",
            "Flushed and re-greased the bearings, laser-aligned the coupling and monitored housing temperature for an hour."),
        new("RTM-BSW-001",
            "Band saw blade wanders in the cut and the guide bearings are noisy.",
            RequestPriority.Medium, 72, 2.5, 1,
            "Blade guide bearings worn and blade tension below specification after the last blade change.",
            "Replaced the upper and lower guide bearings, re-tensioned the blade and checked tracking over a full shift."),
        new("MOS-FLT-001",
            "Forklift will not start on cold mornings; the starter clicks but does not turn.",
            RequestPriority.Medium, 85, 1.5, 2,
            "Corroded battery terminals and a weak starter solenoid contact.",
            "Cleaned and protected the battery terminals, replaced the solenoid contact set and load-tested the battery."),
        new("SBW-FIL-001",
            "Filling line A capping head leaves caps loose on about one bottle in twenty.",
            RequestPriority.Medium, 101, 3, 1,
            "Capping head torque clutch worn and set below the cap specification.",
            "Rebuilt the torque clutch, set it to the cap specification and checked 200 bottles with a torque meter."),
        new("KCC-CMP-002",
            "Air compressor oil carry-over into the air lines; the separator element looks saturated.",
            RequestPriority.Low, 118, 4, 0.75,
            "Oil separator element saturated and the scavenge line orifice partly blocked.",
            "Replaced the separator element, cleared the scavenge orifice and drained oil from the downstream air lines."),
        new("KCC-CMP-002",
            "Air compressor trips on overload during the hot afternoon shift.",
            RequestPriority.High, 128, 1, 0.5,
            "Motor drawing excess current through a clogged intake filter; overload relay contacts starting to pit.",
            "Replaced the intake filter, dressed the relay contacts and confirmed running current below the nameplate through the afternoon."),
        new("SPO-PMP-002",
            "Centrifugal pump flow has dropped and the discharge pressure gauge reads low.",
            RequestPriority.High, 135, 0.75, 1.5,
            "Impeller eye partly blocked with fibre from the clarification tank; wear ring clearance at the limit.",
            "Cleared the impeller, replaced the wear ring and confirmed flow and discharge pressure against the pump curve.")
    };

    // Phase 3 states: New, Approved and Rejected. Four of the Approved rows below are then carried
    // into a WorkOrder by SeedWorkOrdersAsync (their ServiceRequestStatus becomes Assigned, or Approved
    // again for the one whose job gets cancelled); the Centrifugal Pump request is deliberately left
    // Approved-but-unassigned so a reviewer always has one request to assign by hand. Phase 5 adds two
    // Cancelled examples: one cancelled by the creator while still New (decision: ServiceStaff/Admin may
    // undo their own mistake), one cancelled by the Manager after being Approved.
    private record DemoRequest(
        string EquipmentSerial, string Problem, RequestPriority Priority, ServiceRequestStatus Status,
        double CreatedDaysAgo, double? ReviewedDaysAgo = null, string? RejectionReason = null,
        int? CancelledDaysAgo = null, string? CancellationReason = null, bool CancelledByCreator = false);

    private static readonly DemoRequest[] Requests = new DemoRequest[]
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
            RequestPriority.Medium, ServiceRequestStatus.Approved, CreatedDaysAgo: 11, ReviewedDaysAgo: 10),
        new("SBW-FIL-003",
            "Filling line B infeed conveyor stops intermittently and the belt has torn at the splice.",
            RequestPriority.High, ServiceRequestStatus.Approved, CreatedDaysAgo: 4, ReviewedDaysAgo: 4)
    }.Concat(History.Select(h => new DemoRequest(h.EquipmentSerial, h.Problem, h.Priority, ServiceRequestStatus.Approved,
        CreatedDaysAgo: h.CreatedDaysAgo, ReviewedDaysAgo: h.CreatedDaysAgo - h.DaysToAssign / 2))).ToArray();

    // Phase 4 states: one job at each of Assigned, InProgress, Completed and Approved, all worked by
    // the demo Technician, so every status the UI can show — and both the technician's and the
    // Manager's next action — is represented without touching the still-unassigned pump request above.
    // Phase 5 adds one Cancelled job, whose linked request reverts to Approved (BR-12's filtered unique
    // index then lets it be reassigned) — the seeded proof that the revert-and-reassign path works.
    // Phase 7 adds parts used on three jobs and one job paused as PendingParts.
    private record DemoWorkOrder(
        string EquipmentSerial, string Problem, WorkOrderStatus Status, double AssignedDaysAgo,
        double? StartedDaysAgo = null, int? CompletedDaysAgo = null, int? ApprovedDaysAgo = null,
        string? Diagnosis = null, string? WorkPerformed = null,
        int? CancelledDaysAgo = null, string? CancellationReason = null,
        DemoPartUsed[]? PartsUsed = null, DemoPendingParts? Pending = null, int Photos = 0);

    // Phase 7: parts the technician put on a job (each becomes a WorkOrderPart line plus one Issue ledger
    // row), and what a PendingParts job is waiting for.
    private record DemoPartUsed(string PartNumber, decimal Quantity, int DaysAgo);

    private record DemoPendingParts(string PartNumber, decimal Quantity, int DaysAgo, string Reason);

    private static readonly DemoWorkOrder[] WorkOrders = new DemoWorkOrder[]
    {
        // Assigned only: the technician's next job is to tap Start.
        new("RTM-BSW-001",
            "Band saw blade tracking drifts after warm-up, with unusual vibration on the sawmill floor.",
            WorkOrderStatus.Assigned, AssignedDaysAgo: 7),

        // In progress with the evidence already recorded. Since Phase 8 it is deliberately unsigned: the
        // technician's remaining step is to have the customer sign, then tap Complete.
        new("KCC-CMP-002",
            "Air compressor short-cycles under load and trips its overload relay.",
            WorkOrderStatus.InProgress, AssignedDaysAgo: 6, StartedDaysAgo: 5,
            Diagnosis: "Overload relay set point was too low for the compressor's duty cycle; contacts were also pitted.",
            WorkPerformed: "Replaced the overload relay, reset it to the compressor's rated current and ran a 30-minute load test.",
            PartsUsed: new[] { new DemoPartUsed("REL-OVL-25A", 1, 5) },
            Photos: 1),

        // Completed: the Manager's next action is to approve it.
        new("SBW-GEN-002",
            "Standby generator fails to reach rated voltage on load and the AVR indicator flashes.",
            WorkOrderStatus.Completed, AssignedDaysAgo: 10, StartedDaysAgo: 10, CompletedDaysAgo: 9,
            Diagnosis: "AVR sensing lead had a loose crimp, causing voltage regulation to hunt under load.",
            WorkPerformed: "Re-terminated the AVR sensing lead, recalibrated the voltage set point and confirmed with a 3-hour load-bank test.",
            Photos: 2),

        // Approved: the whole workflow closed, shown read-only.
        new("MOS-FLT-001",
            "Forklift hydraulic lift is slow to raise a loaded pallet and leaks a little fluid at the mast.",
            WorkOrderStatus.Approved, AssignedDaysAgo: 16, StartedDaysAgo: 16, CompletedDaysAgo: 15, ApprovedDaysAgo: 14,
            Diagnosis: "Hydraulic pump inlet strainer was clogged and a lift-cylinder seal was worn, causing external leakage.",
            WorkPerformed: "Cleaned the inlet strainer, replaced the lift-cylinder seal kit and topped up hydraulic fluid to spec.",
            PartsUsed: new[] { new DemoPartUsed("SEAL-KIT-HYD", 1, 16), new DemoPartUsed("OIL-HYD-46", 12, 16) },
            Photos: 2),

        // PendingParts (Phase 7): the idler bearings were replaced from stock, but the torn belt needs 6 m of
        // belting and the store has none (the last roll was written off after the store-room leak). A
        // reviewer can receive belting as the warehouse and then resume the job as the technician.
        new("SBW-FIL-003",
            "Filling line B infeed conveyor stops intermittently and the belt has torn at the splice.",
            WorkOrderStatus.PendingParts, AssignedDaysAgo: 3, StartedDaysAgo: 3,
            Diagnosis: "Belt torn at the vulcanised splice; two idler bearings seized, which overloaded the drive and tripped it.",
            WorkPerformed: "Replaced both seized idler bearings. Belt replacement outstanding.",
            PartsUsed: new[] { new DemoPartUsed("BRG-6205-2RS", 2, 3) },
            Pending: new DemoPendingParts("BLT-CNV-600", 6, 3,
                "Need 6 m of 600 mm belting to replace the torn belt; none in stock.")),

        // Cancelled before it could be started: the request behind it reverts to Approved and shows up
        // in the assign queue again, exactly as a real cancel would leave it.
        new("SBW-FIL-001",
            "Filling line A conveyor belt is fraying and needs replacing before it fails completely.",
            WorkOrderStatus.Cancelled, AssignedDaysAgo: 5,
            CancelledDaysAgo: 4,
            CancellationReason: "The parts supplier could not confirm a belt in stock; cancelled so the job can be " +
                                 "reassigned once stock arrives.")
    }.Concat(History.Select(h => new DemoWorkOrder(h.EquipmentSerial, h.Problem, WorkOrderStatus.Approved,
        AssignedDaysAgo: h.AssignedDaysAgo, StartedDaysAgo: h.StartedDaysAgo,
        CompletedDaysAgo: h.CompletedDaysAgo, ApprovedDaysAgo: h.CompletedDaysAgo - 1,
        Diagnosis: h.Diagnosis, WorkPerformed: h.WorkPerformed))).ToArray();

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
        var partIds = await db.Parts.ToDictionaryAsync(p => p.PartNumber, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);

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

            if (demo.Pending is { } pending)
            {
                workOrder.PendingPartsReason = pending.Reason;
                workOrder.PendingPartId = partIds[pending.PartNumber];
                workOrder.PendingPartQuantity = pending.Quantity;
                workOrder.PendingPartsAt = now.AddDays(-pending.DaysAgo);
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

    // Phase 6: a parts catalogue for the equipment above. Every part's stock comes from its own ledger
    // rows (Receive, plus the odd Adjustment with a reason), so QuantityOnHand == SUM(ledger) holds for
    // the seed exactly as it must for the API (BR-09). Four active parts sit at or below their minimum
    // (the low-stock list, AC-09); the discontinued fuse is below its minimum too but inactive, so it
    // proves inactive parts stay off that list. The conveyor belt at zero is the one the cancelled
    // filling-line job was waiting for.
    private record DemoMovement(InventoryTransactionType Type, decimal Quantity, int DaysAgo, string Notes);

    private record DemoPart(
        string PartNumber, string Name, string Description, string Unit, decimal UnitCost,
        decimal MinimumStockLevel, DemoMovement[] Movements, bool IsActive = true);

    private static DemoMovement Received(decimal quantity, int daysAgo, string deliveryNote)
        => new(InventoryTransactionType.Receive, quantity, daysAgo, deliveryNote);

    private static DemoMovement Adjusted(decimal quantity, int daysAgo, string reason)
        => new(InventoryTransactionType.Adjustment, quantity, daysAgo, reason);

    private static readonly DemoPart[] Parts =
    {
        new("REL-OVL-25A", "Thermal overload relay 18–25 A", "Three-pole thermal overload relay for compressor motors.",
            "pcs", 145.00m, 2, new[] { Received(6, 20, "Delivery note DN-24817, Borneo Industrial Supply") }),
        new("FLT-AIR-HC75", "Air filter element, HC-75 compressor", "Intake filter element for Hartmann HC-series compressors.",
            "pcs", 62.00m, 4, new[] { Received(10, 20, "Delivery note DN-24817, Borneo Industrial Supply") }),
        new("OIL-HYD-46", "Hydraulic oil ISO VG 46", "Anti-wear hydraulic oil, decanted from 200 L drums.",
            "L", 14.90m, 80, new[]
            {
                Received(200, 19, "Delivery note DN-24833, Kuching Lubricants"),
                Received(60, 6, "Delivery note DN-25102, Kuching Lubricants"),
                Adjusted(-3.5m, 5, "Spillage while decanting into the workshop dispenser; cleaned up and logged.")
            }),
        new("SEAL-KIT-HYD", "Hydraulic lift-cylinder seal kit", "Seal kit for Tanjung TL-series forklift lift cylinders.",
            "kit", 185.00m, 2, new[] { Received(5, 18, "Delivery note DN-24851, Tanjung Parts Centre") }),
        new("SEAL-MECH-35", "Mechanical seal 35 mm", "Single mechanical seal for Hartmann HP-150 centrifugal pumps.",
            "pcs", 320.00m, 3, new[] { Received(2, 15, "Delivery note DN-24960, Borneo Industrial Supply (part order, 2 of 4)") }),
        new("AVR-SX460", "Automatic voltage regulator SX460", "AVR module for Kestrel KG-series standby generators.",
            "pcs", 690.00m, 1, new[] { Received(1, 14, "Delivery note DN-24977, Sibu Power Systems") }),
        new("BLD-BSW-900", "Band saw blade, 900 series", "Bi-metal blade for Hartmann HB-900 band saws.",
            "pcs", 410.00m, 4, new[] { Received(3, 12, "Delivery note DN-25018, Rajang Saw Services") }),
        new("BRG-6205-2RS", "Deep-groove ball bearing 6205-2RS", "Sealed bearing used across conveyors, pumps and fans.",
            "pcs", 18.40m, 20, new[] { Received(40, 11, "Delivery note DN-25040, Borneo Industrial Supply") }),
        new("SNS-PT100", "PT100 temperature sensor", "Three-wire PT100 probe for kilns and cold rooms.",
            "pcs", 95.00m, 2, new[] { Received(6, 10, "Delivery note DN-25061, Kuching Instrumentation") }),
        new("GSK-BLR-10T", "Boiler manhole gasket", "Graphite manhole gasket for Kestrel KB-10T steam boilers.",
            "pcs", 48.00m, 2, new[] { Received(8, 9, "Delivery note DN-25077, Sibu Power Systems") }),
        new("BLT-CNV-600", "Conveyor belt, 600 mm", "Food-grade PVC conveyor belting for Tanjung TF filling lines, sold by the metre.",
            "m", 88.50m, 10, new[]
            {
                Received(12, 8, "Delivery note DN-25090, Tanjung Parts Centre"),
                Adjusted(-12, 4, "Roll water-damaged in the store-room leak and written off; replacement ordered.")
            }),
        new("FUSE-HRC-63", "HRC fuse 63 A (discontinued)", "Superseded by the 63 A gG fuse; remaining stock is being used up.",
            "pcs", 12.00m, 5, new[] { Received(3, 21, "Opening stock carried over from the old spreadsheet") },
            IsActive: false)
    };

    // Natural key: PartNumber. A part's ledger rows are inserted only together with the part itself, so a
    // re-run never adds a second set of movements to an existing part.
    private static async Task SeedPartsAsync(
        UserManager<ApplicationUser> userManager, ApplicationDbContext db, CancellationToken ct)
    {
        var warehouse = await userManager.FindByEmailAsync("warehouse@sarawakbizops.local")
            ?? throw new InvalidOperationException("Demo WarehouseStaff account is missing.");

        var existing = (await db.Parts.Select(p => p.PartNumber).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var demo in Parts)
        {
            if (!existing.Add(demo.PartNumber))
            {
                continue;
            }

            var part = new Part
            {
                PartNumber = demo.PartNumber,
                Name = demo.Name,
                Description = demo.Description,
                Unit = demo.Unit,
                UnitCost = demo.UnitCost,
                MinimumStockLevel = demo.MinimumStockLevel,
                QuantityOnHand = demo.Movements.Sum(m => m.Quantity),
                IsActive = demo.IsActive,
                CreatedAt = now.AddDays(-demo.Movements.Max(m => m.DaysAgo))
            };

            foreach (var movement in demo.Movements)
            {
                part.Transactions.Add(new InventoryTransaction
                {
                    TransactionType = movement.Type,
                    Quantity = movement.Quantity,
                    CreatedByUserId = warehouse.Id,
                    Notes = movement.Notes,
                    CreatedAt = now.AddDays(-movement.DaysAgo)
                });
            }

            db.Parts.Add(part);
        }

        await db.SaveChangesAsync(ct);
    }

    // Phase 7: each part used becomes a WorkOrderPart line (price snapshotted from the catalogue) plus one
    // Issue ledger row linked to the job, and the part's stock drops by the same amount — exactly what the
    // API does, so QuantityOnHand == SUM(ledger) still holds. Natural key: a job's lines are added only
    // while it has none, so a re-run (or a database seeded before Phase 7) gets each job's parts once.
    private static async Task SeedPartsUsedAsync(
        UserManager<ApplicationUser> userManager, ApplicationDbContext db, CancellationToken ct)
    {
        var technician = await userManager.FindByEmailAsync("technician@sarawakbizops.local")
            ?? throw new InvalidOperationException("Demo Technician account is missing.");

        var workOrders = await db.WorkOrders
            .Include(w => w.ServiceRequest!).ThenInclude(r => r.Equipment)
            .Where(w => w.TechnicianId == technician.Id && !w.PartsUsed.Any())
            .ToListAsync(ct);
        var parts = await db.Parts.ToDictionaryAsync(p => p.PartNumber, StringComparer.OrdinalIgnoreCase, ct);

        var now = DateTime.UtcNow;
        foreach (var demo in WorkOrders.Where(d => d.PartsUsed is not null))
        {
            var workOrder = workOrders.FirstOrDefault(w =>
                w.ServiceRequest!.Equipment!.SerialNumber == demo.EquipmentSerial
                && w.ServiceRequest.ProblemDescription == demo.Problem);
            if (workOrder is null)
            {
                continue;
            }

            foreach (var used in demo.PartsUsed!)
            {
                var part = parts[used.PartNumber];
                if (part.QuantityOnHand < used.Quantity)
                {
                    continue; // never seed negative stock, even on a database whose stock has moved since
                }

                var at = now.AddDays(-used.DaysAgo);
                part.QuantityOnHand -= used.Quantity;
                workOrder.PartsUsed.Add(new WorkOrderPart
                {
                    PartId = part.Id,
                    Quantity = used.Quantity,
                    UnitPrice = part.UnitCost,
                    CreatedAt = at
                });
                db.InventoryTransactions.Add(new InventoryTransaction
                {
                    PartId = part.Id,
                    TransactionType = InventoryTransactionType.Issue,
                    Quantity = -used.Quantity,
                    WorkOrderId = workOrder.Id,
                    CreatedByUserId = technician.Id,
                    Notes = $"Used on work order #{workOrder.Id}",
                    CreatedAt = at
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // Phase 8: BR-03 means every Completed or Approved job carries the customer's signature, so the seeded
    // ones get a fictional scrawl (DemoSignatureImage) uploaded through IFileStorage, exactly as the API
    // stores one. Natural key: only a demo job that has no signature yet gets one, so a re-run adds nothing.
    private static async Task SeedSignaturesAsync(
        UserManager<ApplicationUser> userManager, ApplicationDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var technician = await userManager.FindByEmailAsync("technician@sarawakbizops.local")
            ?? throw new InvalidOperationException("Demo Technician account is missing.");

        var unsigned = await db.WorkOrders
            .Include(w => w.ServiceRequest!).ThenInclude(r => r.Equipment)
            .Where(w => w.TechnicianId == technician.Id && w.SignatureBlobKey == null
                && (w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Approved))
            .ToListAsync(ct);

        foreach (var demo in WorkOrders.Where(d => d.Status is WorkOrderStatus.Completed or WorkOrderStatus.Approved))
        {
            var workOrder = unsigned.FirstOrDefault(w =>
                w.ServiceRequest!.Equipment!.SerialNumber == demo.EquipmentSerial
                && w.ServiceRequest.ProblemDescription == demo.Problem);
            if (workOrder is null)
            {
                continue;
            }

            var png = DemoSignatureImage.Create(demo.EquipmentSerial);
            var key = SignatureRules.NewKey(workOrder.Id);
            using (var content = new MemoryStream(png, writable: false))
            {
                await storage.SaveAsync(key, content, SignatureRules.ContentType, ct);
            }

            workOrder.SignatureBlobKey = key;
            workOrder.SignatureSizeBytes = png.Length;
            workOrder.SignedAt = workOrder.CompletedAt;
        }

        await db.SaveChangesAsync(ct);
    }

    // Phase 9: the in-progress, completed and approved demo jobs carry generated, fictional site photos
    // (DemoPhotoImage — no people, no real sites), uploaded through IFileStorage exactly as the API stores
    // them, so the technician's Photos step and the office gallery both have something to show. Natural key:
    // only a demo job with no photos yet gets them, so a re-run adds nothing.
    private static async Task SeedPhotosAsync(
        UserManager<ApplicationUser> userManager, ApplicationDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var technician = await userManager.FindByEmailAsync("technician@sarawakbizops.local")
            ?? throw new InvalidOperationException("Demo Technician account is missing.");

        var withoutPhotos = await db.WorkOrders
            .Include(w => w.ServiceRequest!).ThenInclude(r => r.Equipment)
            .Where(w => w.TechnicianId == technician.Id && !w.Photos.Any())
            .ToListAsync(ct);

        foreach (var demo in WorkOrders.Where(d => d.Photos > 0))
        {
            var workOrder = withoutPhotos.FirstOrDefault(w =>
                w.ServiceRequest!.Equipment!.SerialNumber == demo.EquipmentSerial
                && w.ServiceRequest.ProblemDescription == demo.Problem);
            if (workOrder is null)
            {
                continue;
            }

            for (var i = 1; i <= demo.Photos; i++)
            {
                var png = DemoPhotoImage.Create($"{demo.EquipmentSerial} photo {i}");
                var key = PhotoRules.NewKey(workOrder.Id, PhotoRules.Png);
                using (var content = new MemoryStream(png, writable: false))
                {
                    await storage.SaveAsync(key, content, PhotoRules.Png, ct);
                }

                db.WorkOrderPhotos.Add(new WorkOrderPhoto
                {
                    WorkOrderId = workOrder.Id,
                    BlobKey = key,
                    ContentType = PhotoRules.Png,
                    SizeBytes = png.Length,
                    UploadedByUserId = technician.Id,
                    // Taken while the job was being worked on, a few minutes apart.
                    UploadedAt = (workOrder.StartedAt ?? DateTime.UtcNow).AddMinutes(20 * i)
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // Phase 10: the Approved demo job gets its service report, generated by the demo Manager through the same
    // IReportService the API uses (a job approved without a report may get exactly one — ReportRules), so the
    // Reports page and the locked state have something to show. The Completed job is left without one so the
    // demo can generate it by hand. Natural key: only a demo job with no report yet gets one.
    private static async Task SeedReportsAsync(
        UserManager<ApplicationUser> userManager, ApplicationDbContext db, IReportService reports, CancellationToken ct)
    {
        var manager = await userManager.FindByEmailAsync("manager@sarawakbizops.local")
            ?? throw new InvalidOperationException("Demo Manager account is missing.");
        var technician = await userManager.FindByEmailAsync("technician@sarawakbizops.local")
            ?? throw new InvalidOperationException("Demo Technician account is missing.");

        var withoutReport = await db.WorkOrders.AsNoTracking()
            .Where(w => w.TechnicianId == technician.Id && w.Status == WorkOrderStatus.Approved && w.ServiceReport == null)
            .Select(w => new { w.Id, w.ServiceRequest!.Equipment!.SerialNumber, w.ServiceRequest.ProblemDescription })
            .ToListAsync(ct);

        foreach (var demo in WorkOrders.Where(d => d.Status == WorkOrderStatus.Approved))
        {
            var workOrder = withoutReport.FirstOrDefault(w =>
                w.SerialNumber == demo.EquipmentSerial && w.ProblemDescription == demo.Problem);
            if (workOrder is null)
            {
                continue;
            }

            var result = await reports.GenerateAsync(workOrder.Id, manager.Id, ct);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not generate the demo report for WorkOrder {workOrder.Id}: {result.ErrorMessage}");
            }
        }
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
