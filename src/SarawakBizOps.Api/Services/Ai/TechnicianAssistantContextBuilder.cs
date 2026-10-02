using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Reports;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>What the builder found: the context the model is shown, plus facts the model is not shown but the answer
/// is enriched with (when each past case was completed, how much of each part is on hand).</summary>
public record TechnicianAssistantInput(
    TechnicianAssistantContext Context,
    IReadOnlyDictionary<int, DateTime> PastCaseCompletedAtUtc,
    IReadOnlyDictionary<int, decimal> PartQuantityOnHand);

/// <summary>
/// The bounded context for the technician assistant (Phase 13). Everything is derived from the one job the caller is
/// allowed to read: its problem and equipment, and past cases from <b>that equipment only</b> — which belongs to the
/// same customer (BR-10) — so no other customer's jobs can reach the model, whatever the prompt says. Every list and
/// every text is capped. Reads go through <see cref="AiReadOnlyData"/>.
/// </summary>
public class TechnicianAssistantContextBuilder
{
    public const int MaxPastCases = 5;
    public const int MaxCandidateParts = 40;
    public const int MaxProblemLength = 1000;
    public const int MaxHistoryTextLength = 400;
    public const int MaxDescriptionLength = 160;
    public const int MaxObservationsLength = 1000;

    private readonly AiReadOnlyData _data;

    public TechnicianAssistantContextBuilder(AiReadOnlyData data) => _data = data;

    /// <returns>null when the job does not exist, or <paramref name="requireOwnTechnician"/> is set and the job is not
    /// the caller's (BR-02: reported as not found, like <c>GET /work-orders/{id}</c>).</returns>
    public async Task<TechnicianAssistantInput?> BuildAsync(
        int workOrderId, string actingUserId, bool requireOwnTechnician, string? observations, CancellationToken ct)
    {
        var job = await _data.WorkOrders
            .Where(w => w.Id == workOrderId)
            .Select(w => new
            {
                w.Id,
                w.TechnicianId,
                w.Status,
                w.Diagnosis,
                w.ServiceRequest!.Priority,
                w.ServiceRequest.ProblemDescription,
                w.ServiceRequest.EquipmentId,
                w.ServiceRequest.Equipment!.EquipmentType,
                w.ServiceRequest.Equipment.Brand,
                w.ServiceRequest.Equipment.Model,
                w.ServiceRequest.Equipment.InstallationDate,
                EquipmentStatus = w.ServiceRequest.Equipment.Status
            })
            .FirstOrDefaultAsync(ct);

        if (job is null || (requireOwnTechnician && job.TechnicianId != actingUserId))
        {
            return null;
        }

        var pastRows = await _data.WorkOrders
            .Where(w => w.ServiceRequest!.EquipmentId == job.EquipmentId
                        && w.Id != job.Id
                        && (w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Approved)
                        && w.CompletedAt != null)
            .OrderByDescending(w => w.CompletedAt)
            .Take(MaxPastCases)
            .Select(w => new
            {
                w.Id,
                CompletedAt = w.CompletedAt!.Value,
                w.ServiceRequest!.ProblemDescription,
                w.Diagnosis,
                w.WorkPerformed,
                Parts = w.PartsUsed.Select(p => new { p.PartId, p.Part!.PartNumber, p.Part.Name }).ToList()
            })
            .ToListAsync(ct);

        // Parts used on this equipment before come first, then the rest of the active catalogue, up to the cap.
        var usedBefore = pastRows.SelectMany(r => r.Parts.Select(p => p.PartId)).ToHashSet();
        var parts = await _data.Parts
            .Where(p => p.IsActive)
            .OrderBy(p => usedBefore.Contains(p.Id) ? 0 : 1)
            .ThenBy(p => p.PartNumber)
            .Take(MaxCandidateParts)
            .Select(p => new { p.Id, p.PartNumber, p.Name, p.Description, p.Unit, p.QuantityOnHand })
            .ToListAsync(ct);

        var context = new TechnicianAssistantContext(
            new AssistantJob(
                job.Id,
                job.Status.ToString(),
                job.Priority.ToString(),
                Cut(job.ProblemDescription, MaxProblemLength)!,
                Cut(job.Diagnosis, MaxHistoryTextLength),
                Cut(observations, MaxObservationsLength)),
            new AssistantEquipment(
                job.EquipmentType,
                job.Brand,
                job.Model,
                job.InstallationDate?.ToString("yyyy-MM-dd"),
                job.EquipmentStatus.ToString()),
            pastRows.Select(r => new AssistantPastCase(
                r.Id,
                ReportRules.ToMyt(r.CompletedAt).ToString("yyyy-MM-dd"),
                Cut(r.ProblemDescription, MaxHistoryTextLength)!,
                Cut(r.Diagnosis, MaxHistoryTextLength) ?? "",
                Cut(r.WorkPerformed, MaxHistoryTextLength) ?? "",
                r.Parts.Select(p => $"{p.PartNumber} {p.Name}").Distinct().ToList())).ToList(),
            parts.Select(p => new AssistantCandidatePart(
                p.Id, p.PartNumber, p.Name, Cut(p.Description, MaxDescriptionLength), p.Unit)).ToList());

        return new TechnicianAssistantInput(
            context,
            pastRows.ToDictionary(r => r.Id, r => r.CompletedAt),
            parts.ToDictionary(p => p.Id, p => p.QuantityOnHand));
    }

    private static string? Cut(string? text, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)].TrimEnd() + "…";
    }
}
