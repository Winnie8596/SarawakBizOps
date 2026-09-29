using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Services.Reports;

/// <summary>
/// The service report's rules (Phase 10, PRD §6.7, BR-14 per D-05) as a pure class, unit-testable without a
/// database or storage: when a report may be generated, how it is numbered and where its file goes.
/// </summary>
public static class ReportRules
{
    public const string ContentType = "application/pdf";

    /// <summary>Malaysia has one time zone and no daylight saving, so MYT is a fixed UTC+8 (PRD Q-02).</summary>
    public static readonly TimeSpan MytOffset = TimeSpan.FromHours(8);

    /// <summary>JPEG photos are embedded as they are (no re-encoding), so their total is capped to keep the
    /// PDF a sensible size; photos past the cap are listed but not pictured.</summary>
    public const long MaxEmbeddedJpegBytes = 16L * 1024 * 1024;

    /// <summary>PNG photos are decoded and shrunk to this longer side — a thumbnail, not the original.</summary>
    public const int PhotoThumbnailMaxSide = 480;

    public const int SignatureMaxSide = 800;

    /// <summary>A UTC timestamp as Malaysia wall-clock time.</summary>
    public static DateTime ToMyt(DateTime utc) => DateTime.SpecifyKind(utc + MytOffset, DateTimeKind.Unspecified);

    /// <summary>
    /// <c>SR-{year}-{sequence}</c>, e.g. SR-2026-000123. The sequence comes from one database SEQUENCE and never
    /// restarts, so numbers are unique across years and under parallel generation; the year (MYT, when the
    /// number was first issued) is there for people.
    /// </summary>
    public static string FormatNumber(int year, long sequence) => $"SR-{year}-{sequence:D6}";

    /// <summary>A fresh storage key for every rendering, so a regeneration never overwrites the file that the
    /// database still points at until it commits.</summary>
    public static string NewKey(int workOrderId) => $"work-orders/{workOrderId}/report-{Guid.NewGuid():N}.pdf";

    /// <summary>
    /// Why a report cannot be generated now, or null when it can:
    /// <list type="bullet">
    /// <item>a <b>Completed</b> job — any number of times; each run replaces the file and bumps the revision
    /// while keeping the report number;</item>
    /// <item>an <b>Approved</b> job that has no report yet (e.g. approved before reports existed) — once: its data
    /// is locked, so that report can never differ from one made before approval;</item>
    /// <item>never after that: approval locks the report version (BR-14 per D-05, no reopen flow).</item>
    /// </list>
    /// </summary>
    public static string? GenerateRefusal(WorkOrderStatus status, string? existingReportNumber) => status switch
    {
        WorkOrderStatus.Completed => null,
        WorkOrderStatus.Approved when existingReportNumber is null => null,
        WorkOrderStatus.Approved =>
            $"This job is Approved, so its service report {existingReportNumber} is locked and cannot be regenerated.",
        WorkOrderStatus.Cancelled => "This job is Cancelled; a cancelled job has no service report.",
        _ => $"This job is {status}; a service report can only be generated once the job is completed."
    };
}
