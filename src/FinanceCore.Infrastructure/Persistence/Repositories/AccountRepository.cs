using Microsoft.EntityFrameworkCore;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Persistence.Repositories;

public class AccountRepository : IAccountRepository
{
    private readonly FinanceCoreDbContext _context;

    public AccountRepository(FinanceCoreDbContext context) => _context = context;

    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Accounts.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<Account>> GetAllAsync(
        System.Linq.Expressions.Expression<Func<Account, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.Accounts.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.ToListAsync(ct);
    }

    public void Add(Account entity) => _context.Accounts.Add(entity);
    public void AddRange(IEnumerable<Account> entities) => _context.Accounts.AddRange(entities);
    public void Update(Account entity) => _context.Accounts.Update(entity);
    public void Remove(Account entity) => _context.Accounts.Remove(entity);

    public async Task<bool> ExistsAsync(
        System.Linq.Expressions.Expression<Func<Account, bool>> predicate,
        CancellationToken ct = default)
        => await _context.Accounts.AnyAsync(predicate, ct);

    public async Task<int> CountAsync(
        System.Linq.Expressions.Expression<Func<Account, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.Accounts.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.CountAsync(ct);
    }

    public async Task<Account?> GetByAccountNumberAsync(string accountNumber, Guid institutionId, CancellationToken ct = default)
        => await _context.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == accountNumber && a.InstitutionId == institutionId, ct);

    public async Task<IReadOnlyList<Account>> GetActiveAccountsAsync(CancellationToken ct = default)
        => await _context.Accounts.Where(a => a.IsActive).ToListAsync(ct);

    public async Task<IReadOnlyList<Account>> GetByInstitutionAsync(Guid institutionId, CancellationToken ct = default)
        => await _context.Accounts.Where(a => a.InstitutionId == institutionId).ToListAsync(ct);

    public async Task<Account?> GetByIdWithLockAsync(Guid id, int expectedVersion, CancellationToken ct = default)
        => await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.Version == expectedVersion, ct);

    public async Task<IReadOnlyDictionary<string, decimal>> GetTotalBalancesByCurrencyAsync(CancellationToken ct = default)
    {
        // Currency y CurrentBalance son Value Objects ignorados en el mapping;
        // EF sólo conoce los backing fields _currencyCode y _currentBalanceValue.
        // Usamos EF.Property<>() para que el provider pueda traducir el GroupBy/Sum
        // a SQL puro (de otro modo lanza "could not be translated").
        var balances = await _context.Accounts
            .Where(a => a.IsActive)
            .GroupBy(a => EF.Property<string>(a, "_currencyCode"))
            .Select(g => new
            {
                Currency = g.Key,
                Total = g.Sum(a => EF.Property<decimal>(a, "_currentBalanceValue"))
            })
            .ToListAsync(ct);

        return balances.ToDictionary(b => b.Currency!, b => b.Total);
    }
}
