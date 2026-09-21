using Microsoft.EntityFrameworkCore;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.History;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Services.History;

public class ServiceHistoryService : IServiceHistoryService
{
    private const int SummaryLength = 120;

    private readonly ApplicationDbContext _db;

    public ServiceHistoryService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ServiceHistoryDto?> ForCustomerAsync(int customerId, CancellationToken ct)
    {
        if (!await _db.Customers.AnyAsync(c => c.Id == customerId, ct))
        {
            return null;
        }

        return await BuildAsync(_db.ServiceRequests.Where(r => r.CustomerId == customerId), ct);
    }

    public async Task<ServiceHistoryDto?> ForEquipmentAsync(int equipmentId, CancellationToken ct)
    {
        if (!await _db.Equipment.AnyAsync(e => e.Id == equipmentId, ct))
        {
            return null;
        }

        return await BuildAsync(_db.ServiceRequests.Where(r => r.EquipmentId == equipmentId), ct);
    }

    private static async Task<ServiceHistoryDto> BuildAsync(IQueryable<ServiceRequest> requests, CancellationToken ct)
    {
        var rows = await requests
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return new ServiceHistoryDto
        {
            Items = rows.Select(r => new ServiceHistoryItemDto
            {
                Type = "ServiceRequest",
                Id = r.Id,
                Status = r.Status.ToString(),
                Priority = r.Priority.ToString(),
                Summary = r.ProblemDescription.Length <= SummaryLength
                    ? r.ProblemDescription
                    : r.ProblemDescription[..SummaryLength] + "…",
                EquipmentId = r.EquipmentId,
                OccurredAtUtc = r.CreatedAt
            }).ToList()
        };
    }
}
