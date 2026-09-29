namespace SarawakBizOps.Api.Services.Reports;

/// <summary>
/// Everything a service report shows (PRD §6.7), taken from the completed work order by
/// <see cref="ServiceReportBuilder"/> and then drawn by <see cref="ServiceReportRenderer"/>. Tests assert on this
/// model rather than on PDF bytes. Times are already in Malaysia time (MYT, UTC+8 — PRD Q-02) and money in RM
/// rounded to sen, so the renderer only formats.
/// </summary>
public sealed record ServiceReportModel
{
    public required string ReportNumber { get; init; }
    public required int Revision { get; init; }
    public required DateTime GeneratedAtMyt { get; init; }
    public required string GeneratedByName { get; init; }

    public required int WorkOrderId { get; init; }
    public required int ServiceRequestId { get; init; }

    public required ReportCustomer Customer { get; init; }
    public required ReportEquipment Equipment { get; init; }

    public required string Priority { get; init; }
    public required string ProblemDescription { get; init; }
    public required string TechnicianName { get; init; }

    public required string Diagnosis { get; init; }
    public required string WorkPerformed { get; init; }
    public string? Notes { get; init; }

    public required IReadOnlyList<ReportPartLine> Parts { get; init; }
    public required decimal PartsTotal { get; init; }

    /// <summary>Oldest first, as on the work order.</summary>
    public required IReadOnlyList<ReportPhoto> Photos { get; init; }

    public required bool HasSignature { get; init; }

    /// <summary>The key moments of the job, oldest first; steps the job never went through are left out.</summary>
    public required IReadOnlyList<ReportTimestamp> Timeline { get; init; }
}

public sealed record ReportCustomer(string CompanyName, string? ContactPerson, string? Phone, string? Email, string? Address);

public sealed record ReportEquipment(
    string SerialNumber, string EquipmentType, string? Brand, string? Model, string? Location);

public sealed record ReportPartLine(
    string PartNumber, string PartName, string Unit, decimal Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary><see cref="Number"/> is the photo's 1-based position on the job.</summary>
public sealed record ReportPhoto(int Number, int PhotoId, string ContentType, DateTime TakenAtMyt, string TakenByName);

public sealed record ReportTimestamp(string Label, DateTime AtMyt);
