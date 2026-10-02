namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// Everything the model is shown for one technician-assistant question (Phase 13, AI-01) — and nothing else. Built by
/// <see cref="TechnicianAssistantContextBuilder"/> from the job alone: the problem, the equipment record and that
/// equipment's own earlier jobs, plus the active parts catalogue to pick from. No customer name, contact or address,
/// no serial number or location, and no other customer's jobs ever reach it.
/// </summary>
public record TechnicianAssistantContext(
    AssistantJob Job,
    AssistantEquipment Equipment,
    IReadOnlyList<AssistantPastCase> PastCases,
    IReadOnlyList<AssistantCandidatePart> CandidateParts);

/// <param name="TechnicianObservations">What the technician typed into the assistant (free text, data only).</param>
public record AssistantJob(
    int WorkOrderId, string Status, string Priority, string ProblemDescription,
    string? DiagnosisSoFar, string? TechnicianObservations);

public record AssistantEquipment(string EquipmentType, string? Brand, string? Model, string? InstalledOn, string Status);

/// <summary>An earlier Completed or Approved job on the same equipment. CompletedOn is the MYT date.</summary>
public record AssistantPastCase(
    int WorkOrderId, string CompletedOn, string Problem, string Diagnosis, string WorkPerformed,
    IReadOnlyList<string> PartsUsed);

/// <summary>An active catalogue part the model may point to (by <see cref="PartId"/> only).</summary>
public record AssistantCandidatePart(int PartId, string PartNumber, string Name, string? Description, string Unit);
