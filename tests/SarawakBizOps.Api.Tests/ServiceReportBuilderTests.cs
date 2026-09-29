using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Models.Enums;
using SarawakBizOps.Api.Services.Reports;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Phase 10: the report's content is asserted on its data model (PRD §6.7) — customer, equipment, problem,
/// priority, diagnosis, work performed, parts with RM totals, photos, signature, technician and the key
/// timestamps in MYT — rather than on PDF bytes.
/// </summary>
public class ServiceReportBuilderTests
{
    private static DateTime Utc(int month, int day, int hour, int minute)
        => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static WorkOrder CompletedJob()
    {
        var technician = new ApplicationUser { Id = "tech-1", FullName = "Ahmad Bujang" };
        var relay = new Part { Id = 301, PartNumber = "REL-OVL-25A", Name = "Thermal overload relay 18–25 A", Unit = "pcs" };
        var oil = new Part { Id = 302, PartNumber = "OIL-HYD-46", Name = "Hydraulic oil ISO 46", Unit = "L" };

        return new WorkOrder
        {
            Id = 41,
            Status = WorkOrderStatus.Completed,
            ServiceRequest = new ServiceRequest
            {
                Id = 7,
                Priority = RequestPriority.High,
                ProblemDescription = "Blade tracking drifts after warm-up.",
                CreatedAt = Utc(9, 19, 23, 50),
                ApprovedAt = Utc(9, 20, 1, 0),
                Customer = new Customer
                {
                    CompanyName = "Rajang Timber Mills Sdn Bhd",
                    ContactPerson = "  ",
                    Phone = "084-000 111",
                    Email = null,
                    Address = " Lot 12, Jalan Industri, Sibu "
                },
                Equipment = new Equipment
                {
                    SerialNumber = "RTM-BSW-001", EquipmentType = "Band Saw", Brand = "Wintersteiger", Model = "",
                    Location = "Sawmill line A"
                }
            },
            Technician = technician,
            TechnicianId = technician.Id,
            AssignedAt = Utc(9, 20, 2, 0),
            StartedAt = Utc(9, 21, 0, 15),
            CompletedAt = Utc(9, 21, 16, 30), // 00:30 the next day in MYT
            Diagnosis = "Tracking wheel bearing worn.",
            WorkPerformed = "Replaced the bearing and re-tracked the blade.",
            Notes = "   ",
            SignatureBlobKey = "work-orders/41/signature-x.png",
            SignedAt = Utc(9, 21, 16, 20),
            PartsUsed =
            {
                // Out of order on purpose: the report lists them oldest first.
                new WorkOrderPart { Id = 12, Part = oil, Quantity = 3m, UnitPrice = 0.335m, CreatedAt = Utc(9, 21, 3, 0) },
                new WorkOrderPart { Id = 11, Part = relay, Quantity = 2m, UnitPrice = 1450.50m, CreatedAt = Utc(9, 21, 2, 0) }
            },
            Photos =
            {
                new WorkOrderPhoto { Id = 902, ContentType = "image/png", UploadedAt = Utc(9, 21, 4, 0), UploadedByUser = technician },
                new WorkOrderPhoto { Id = 901, ContentType = "image/jpeg", UploadedAt = Utc(9, 21, 1, 0), UploadedByUser = technician }
            }
        };
    }

    [Fact]
    public void The_model_carries_the_report_identity_and_who_generated_it_in_malaysia_time()
    {
        var model = ServiceReportBuilder.Build(CompletedJob(), "SR-2026-000123", 2, Utc(9, 22, 2, 5), "Jenny Lau");

        Assert.Equal(("SR-2026-000123", 2, "Jenny Lau"), (model.ReportNumber, model.Revision, model.GeneratedByName));
        Assert.Equal(new DateTime(2026, 9, 22, 10, 5, 0), model.GeneratedAtMyt);
        Assert.Equal((41, 7), (model.WorkOrderId, model.ServiceRequestId));
    }

    [Fact]
    public void Customer_equipment_and_job_details_come_from_the_work_order_with_blank_fields_left_out()
    {
        var model = ServiceReportBuilder.Build(CompletedJob(), "SR-2026-000123", 1, Utc(9, 22, 2, 5), "Jenny Lau");

        Assert.Equal(new ReportCustomer("Rajang Timber Mills Sdn Bhd", null, "084-000 111", null, "Lot 12, Jalan Industri, Sibu"),
            model.Customer);
        Assert.Equal(new ReportEquipment("RTM-BSW-001", "Band Saw", "Wintersteiger", null, "Sawmill line A"), model.Equipment);
        Assert.Equal("High", model.Priority);
        Assert.Equal("Blade tracking drifts after warm-up.", model.ProblemDescription);
        Assert.Equal("Ahmad Bujang", model.TechnicianName);
        Assert.Equal("Tracking wheel bearing worn.", model.Diagnosis);
        Assert.Equal("Replaced the bearing and re-tracked the blade.", model.WorkPerformed);
        Assert.Null(model.Notes);
        Assert.True(model.HasSignature);
    }

    [Fact]
    public void Parts_are_listed_oldest_first_with_line_totals_rounded_to_sen_and_an_rm_total()
    {
        var model = ServiceReportBuilder.Build(CompletedJob(), "SR-2026-000123", 1, Utc(9, 22, 2, 5), "Jenny Lau");

        Assert.Equal(new[]
        {
            new ReportPartLine("REL-OVL-25A", "Thermal overload relay 18–25 A", "pcs", 2m, 1450.50m, 2901.00m),
            new ReportPartLine("OIL-HYD-46", "Hydraulic oil ISO 46", "L", 3m, 0.335m, 1.01m) // 1.005 rounds away from zero
        }, model.Parts);
        Assert.Equal(2902.01m, model.PartsTotal);
    }

    [Fact]
    public void Photos_are_numbered_in_the_order_they_were_taken()
    {
        var model = ServiceReportBuilder.Build(CompletedJob(), "SR-2026-000123", 1, Utc(9, 22, 2, 5), "Jenny Lau");

        Assert.Equal(new[]
        {
            new ReportPhoto(1, 901, "image/jpeg", new DateTime(2026, 9, 21, 9, 0, 0), "Ahmad Bujang"),
            new ReportPhoto(2, 902, "image/png", new DateTime(2026, 9, 21, 12, 0, 0), "Ahmad Bujang")
        }, model.Photos);
    }

    [Fact]
    public void The_timeline_is_in_malaysia_time_and_skips_steps_the_job_never_went_through()
    {
        var model = ServiceReportBuilder.Build(CompletedJob(), "SR-2026-000123", 1, Utc(9, 22, 2, 5), "Jenny Lau");

        Assert.Equal(new[]
        {
            new ReportTimestamp("Requested", new DateTime(2026, 9, 20, 7, 50, 0)),
            new ReportTimestamp("Request approved", new DateTime(2026, 9, 20, 9, 0, 0)),
            new ReportTimestamp("Assigned", new DateTime(2026, 9, 20, 10, 0, 0)),
            new ReportTimestamp("Started", new DateTime(2026, 9, 21, 8, 15, 0)),
            new ReportTimestamp("Customer signed", new DateTime(2026, 9, 22, 0, 20, 0)),
            new ReportTimestamp("Completed", new DateTime(2026, 9, 22, 0, 30, 0))
        }, model.Timeline);
    }

    [Fact]
    public void A_job_that_waited_for_parts_shows_the_wait_and_the_resume()
    {
        var job = CompletedJob();
        job.PendingPartsAt = Utc(9, 21, 1, 0);
        job.ResumedAt = Utc(9, 21, 2, 0);

        var labels = ServiceReportBuilder.Build(job, "SR-2026-000123", 1, Utc(9, 22, 2, 5), "Jenny Lau")
            .Timeline.Select(t => t.Label);

        Assert.Equal(new[] { "Requested", "Request approved", "Assigned", "Started", "Waiting for parts", "Resumed", "Customer signed", "Completed" },
            labels);
    }

    [Fact]
    public void A_job_without_parts_or_photos_has_empty_lists_and_a_zero_total()
    {
        var job = CompletedJob();
        job.PartsUsed.Clear();
        job.Photos.Clear();

        var model = ServiceReportBuilder.Build(job, "SR-2026-000123", 1, Utc(9, 22, 2, 5), "Jenny Lau");

        Assert.Empty(model.Parts);
        Assert.Equal(0m, model.PartsTotal);
        Assert.Empty(model.Photos);
    }

    [Fact]
    public void Building_needs_the_request_customer_and_equipment_loaded()
    {
        var job = CompletedJob();
        job.ServiceRequest!.Customer = null;

        Assert.Throws<ArgumentException>(() => ServiceReportBuilder.Build(job, "SR-2026-000123", 1, Utc(9, 22, 2, 5), "Jenny Lau"));
    }
}
