using Microsoft.EntityFrameworkCore;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Persistence.Repositories;

public class ReconciliationRepository : IReconciliationRepository
{
    private readonly FinanceCoreDbContext _context;

    public ReconciliationRepository(FinanceCoreDbContext context)
    {
        _context = context;
    }

    public async Task<Reconciliation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Reconciliations
            .Include(r => r.Discrepancies)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<Reconciliation?> GetByAccountAndDateAsync(
        Guid accountId,
        DateOnly date,
        CancellationToken ct = default)
        => await _context.Reconciliations
            .Include(r => r.Discrepancies)
            .FirstOrDefaultAsync(r => r.AccountId == accountId && r.ReconciliationDate == date, ct);

    public async Task<IReadOnlyList<Reconciliation>> GetByAccountAsync(
        Guid accountId,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var query = _context.Reconciliations
            .Include(r => r.Discrepancies)
            .Where(r => r.AccountId == accountId);

        if (startDate.HasValue)
            query = query.Where(r => r.ReconciliationDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(r => r.ReconciliationDate <= endDate.Value);

        return await query
            .OrderByDescending(r => r.ReconciliationDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Reconciliation>> SearchAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        ReconciliationStatus? status = null,
        Guid? accountId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = _context.Reconciliations.AsQueryable();

        if (accountId.HasValue)
            query = query.Where(r => r.AccountId == accountId.Value);
        if (startDate.HasValue)
            query = query.Where(r => r.ReconciliationDate >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(r => r.ReconciliationDate <= endDate.Value);
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var skip = Math.Max(0, (page - 1) * pageSize);
        return await query
            .OrderByDescending(r => r.ReconciliationDate)
            .Skip(skip)
            .Take(Math.Clamp(pageSize, 1, 500))
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        ReconciliationStatus? status = null,
        Guid? accountId = null,
        CancellationToken ct = default)
    {
        var query = _context.Reconciliations.AsQueryable();

        if (accountId.HasValue)
            query = query.Where(r => r.AccountId == accountId.Value);
        if (startDate.HasValue)
            query = query.Where(r => r.ReconciliationDate >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(r => r.ReconciliationDate <= endDate.Value);
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        return await query.CountAsync(ct);
    }

    public async Task<IReadOnlyList<ReconciliationDiscrepancy>> GetDiscrepanciesAsync(
        Guid reconciliationId,
        CancellationToken ct = default)
        => await _context.ReconciliationDiscrepancies
            .Where(d => d.ReconciliationId == reconciliationId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

    public void Add(Reconciliation reconciliation) => _context.Reconciliations.Add(reconciliation);
    public void Update(Reconciliation reconciliation) => _context.Reconciliations.Update(reconciliation);
    public void Remove(Reconciliation reconciliation) => _context.Reconciliations.Remove(reconciliation);
}
