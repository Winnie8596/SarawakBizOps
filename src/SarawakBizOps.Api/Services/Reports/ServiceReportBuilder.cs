using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Services.Inventory;

namespace SarawakBizOps.Api.Services.Reports;

/// <summary>
/// Turns a work order — loaded with its request, customer, equipment, technician, parts and photos — into the
/// <see cref="ServiceReportModel"/>. Pure (no database, clock or storage), so the report's content is checked
/// field by field in unit tests.
/// </summary>
public static class ServiceReportBuilder
{
    public static ServiceReportModel Build(
        WorkOrder workOrder, string reportNumber, int revision, DateTime generatedAtUtc, string generatedByName)
    {
        var request = workOrder.ServiceRequest
            ?? throw new ArgumentException("The work order must be loaded with its service request.", nameof(workOrder));
        var customer = request.Customer
            ?? throw new ArgumentException("The work order must be loaded with its customer.", nameof(workOrder));
        var equipment = request.Equipment
            ?? throw new ArgumentException("The work order must be loaded with its equipment.", nameof(workOrder));

        var parts = workOrder.PartsUsed
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Select(p => new ReportPartLine(
                p.Part?.PartNumber ?? string.Empty,
                p.Part?.Name ?? string.Empty,
                p.Part?.Unit ?? string.Empty,
                p.Quantity,
                p.UnitPrice,
                StockMovementRules.LineTotal(p.Quantity, p.UnitPrice)))
            .ToList();

        var photos = workOrder.Photos
            .OrderBy(p => p.UploadedAt)
            .ThenBy(p => p.Id)
            .Select((p, index) => new ReportPhoto(
                index + 1, p.Id, p.ContentType, ReportRules.ToMyt(p.UploadedAt), p.UploadedByUser?.FullName ?? string.Empty))
            .ToList();

        var timeline = new (string Label, DateTime? At)[]
            {
                ("Requested", request.CreatedAt),
                ("Request approved", request.ApprovedAt),
                ("Assigned", workOrder.AssignedAt),
                ("Started", workOrder.StartedAt),
                ("Waiting for parts", workOrder.PendingPartsAt),
                ("Resumed", workOrder.ResumedAt),
                ("Customer signed", workOrder.SignedAt),
                ("Completed", workOrder.CompletedAt)
            }
            .Where(t => t.At is not null)
            .Select(t => new ReportTimestamp(t.Label, ReportRules.ToMyt(t.At!.Value)))
            .ToList();

        return new ServiceReportModel
        {
            ReportNumber = reportNumber,
            Revision = revision,
            GeneratedAtMyt = ReportRules.ToMyt(generatedAtUtc),
            GeneratedByName = generatedByName,
            WorkOrderId = workOrder.Id,
            ServiceRequestId = request.Id,
            Customer = new ReportCustomer(
                customer.CompanyName, Blank(customer.ContactPerson), Blank(customer.Phone), Blank(customer.Email),
                Blank(customer.Address)),
            Equipment = new ReportEquipment(
                equipment.SerialNumber, equipment.EquipmentType, Blank(equipment.Brand), Blank(equipment.Model),
                Blank(equipment.Location)),
            Priority = request.Priority.ToString(),
            ProblemDescription = request.ProblemDescription,
            TechnicianName = workOrder.Technician?.FullName ?? string.Empty,
            Diagnosis = workOrder.Diagnosis ?? string.Empty,
            WorkPerformed = workOrder.WorkPerformed ?? string.Empty,
            Notes = Blank(workOrder.Notes),
            Parts = parts,
            PartsTotal = parts.Sum(p => p.LineTotal),
            Photos = photos,
            HasSignature = workOrder.SignatureBlobKey is not null,
            Timeline = timeline
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
