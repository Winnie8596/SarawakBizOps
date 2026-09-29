using SarawakBizOps.Api.DTOs.Reports;
using SarawakBizOps.Api.DTOs.WorkOrders;
using SarawakBizOps.Api.Services.Common;
using SarawakBizOps.Api.Services.Storage;

namespace SarawakBizOps.Api.Services.Reports;

/// <summary>Phase 10: the service report PDF (PRD §6.7), rendered from the completed job and stored through
/// <see cref="IFileStorage"/>.</summary>
public interface IReportService
{
    /// <summary>
    /// Renders the job's report and stores it. The first run issues a unique report number; while the job is
    /// Completed, running again replaces the file and bumps the revision. Refused once the job is Approved and has
    /// a report (BR-14 per D-05) — see <see cref="ReportRules.GenerateRefusal"/>. Returns the job with its report.
    /// </summary>
    Task<ServiceResult<WorkOrderDto>> GenerateAsync(int workOrderId, string actingUserId, CancellationToken ct);

    /// <summary>
    /// The stored PDF and its report number. <paramref name="requireOwnTechnician"/> hides another technician's job
    /// as not-found, exactly like reading the job itself.
    /// </summary>
    Task<ServiceResult<(StoredFile File, string ReportNumber)>> OpenAsync(
        int workOrderId, string actingUserId, bool requireOwnTechnician, CancellationToken ct);

    /// <summary>Every report, newest first.</summary>
    Task<IReadOnlyList<ServiceReportDto>> GetAllAsync(CancellationToken ct);
}
