using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Services.WorkOrders;

/// <summary>
/// The Phase 5 conflict guard for changing what is on an in-progress job (its parts, Phase 7, and its photos,
/// Phase 9), shared so both services claim the job the same way.
/// </summary>
public static class JobClaims
{
    /// <summary>
    /// A no-op UPDATE of the job that matches only while it is still <paramref name="technicianId"/>'s, still
    /// InProgress, still at the caller's <paramref name="rowVersion"/> and meets <paramref name="alsoRequire"/>.
    /// Matching bumps the RowVersion (the job changed, so any screen showing it is now stale) and holds the job
    /// row's lock until the caller's transaction commits, so a Manager's cancel or the technician's own Complete
    /// cannot interleave with the change. Call it inside that transaction; false means the claim was lost.
    /// </summary>
    public static async Task<bool> ClaimInProgressJobAsync(
        this ApplicationDbContext db, int workOrderId, string technicianId, byte[] rowVersion, CancellationToken ct,
        Expression<Func<WorkOrder, bool>>? alsoRequire = null)
    {
        var job = db.WorkOrders.Where(w => w.Id == workOrderId
                                           && w.TechnicianId == technicianId
                                           && w.Status == WorkOrderStatus.InProgress
                                           && w.RowVersion == rowVersion);
        if (alsoRequire is not null)
        {
            job = job.Where(alsoRequire);
        }

        return await job.ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, w => w.Status), ct) == 1;
    }
}
