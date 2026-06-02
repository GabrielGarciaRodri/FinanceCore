using Microsoft.EntityFrameworkCore;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Persistence.Repositories;

public class DailyBalanceRepository : IDailyBalanceRepository
{
    private readonly FinanceCoreDbContext _context;

    public DailyBalanceRepository(FinanceCoreDbContext context) => _context = context;

    public async Task<DailyBalance?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.DailyBalances.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<DailyBalance>> GetAllAsync(
        System.Linq.Expressions.Expression<Func<DailyBalance, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.DailyBalances.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.ToListAsync(ct);
    }

    public void Add(DailyBalance entity) => _context.DailyBalances.Add(entity);
    public void AddRange(IEnumerable<DailyBalance> entities) => _context.DailyBalances.AddRange(entities);
    public void Update(DailyBalance entity) => _context.DailyBalances.Update(entity);
    public void Remove(DailyBalance entity) => _context.DailyBalances.Remove(entity);

    public async Task<bool> ExistsAsync(
        System.Linq.Expressions.Expression<Func<DailyBalance, bool>> predicate,
        CancellationToken ct = default)
        => await _context.DailyBalances.AnyAsync(predicate, ct);

    public async Task<int> CountAsync(
        System.Linq.Expressions.Expression<Func<DailyBalance, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.DailyBalances.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.CountAsync(ct);
    }

    public async Task<DailyBalance?> GetByAccountAndDateAsync(Guid accountId, DateOnly date, CancellationToken ct = default)
        => await _context.DailyBalances.FirstOrDefaultAsync(d => d.AccountId == accountId && d.BalanceDate == date, ct);

    public async Task<IReadOnlyList<DailyBalance>> GetByAccountAndDateRangeAsync(
        Guid accountId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
        => await _context.DailyBalances
            .Where(d => d.AccountId == accountId && d.BalanceDate >= startDate && d.BalanceDate <= endDate)
            .OrderBy(d => d.BalanceDate)
            .ToListAsync(ct);

    public async Task<DailyBalance?> GetLatestByAccountAsync(Guid accountId, CancellationToken ct = default)
        => await _context.DailyBalances
            .Where(d => d.AccountId == accountId)
            .OrderByDescending(d => d.BalanceDate)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<DailyBalance>> GetUnreconciledAsync(DateOnly? beforeDate = null, CancellationToken ct = default)
    {
        var query = _context.DailyBalances.Where(d => !d.IsReconciled);
        if (beforeDate.HasValue) query = query.Where(d => d.BalanceDate < beforeDate.Value);
        return await query.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DailyBalance>> GetMissingDatesAsync(
        Guid accountId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        if (endDate < startDate)
            throw new ArgumentException("endDate debe ser mayor o igual a startDate.");

        var existingDates = await _context.DailyBalances
            .Where(d => d.AccountId == accountId && d.BalanceDate >= startDate && d.BalanceDate <= endDate)
            .Select(d => d.BalanceDate)
            .ToListAsync(ct);

        var existingSet = existingDates.ToHashSet();
        var missing = new List<DailyBalance>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            ct.ThrowIfCancellationRequested();
            if (existingSet.Contains(date))
                continue;

            missing.Add(DailyBalance.CreateEmpty(accountId, date));
        }

        return missing;
    }

    public async Task UpsertAsync(DailyBalance balance, CancellationToken ct = default)
    {
        var existing = await GetByAccountAndDateAsync(balance.AccountId, balance.BalanceDate, ct);
        if (existing != null)
        {
            existing.Update(balance.OpeningBalance, balance.ClosingBalance, balance.TotalDebits, balance.TotalCredits, balance.TransactionCount);
            Update(existing);
        }
        else
        {
            Add(balance);
        }
    }
}
