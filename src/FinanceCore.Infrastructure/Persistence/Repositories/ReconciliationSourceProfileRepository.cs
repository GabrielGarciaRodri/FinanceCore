using Microsoft.EntityFrameworkCore;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Persistence.Repositories;

public class ReconciliationSourceProfileRepository : IReconciliationSourceProfileRepository
{
    private readonly FinanceCoreDbContext _context;

    public ReconciliationSourceProfileRepository(FinanceCoreDbContext context)
    {
        _context = context;
    }

    public async Task<ReconciliationSourceProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.ReconciliationSourceProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<ReconciliationSourceProfile>> GetActiveForAccountAsync(
        Guid accountId,
        CancellationToken ct = default)
        => await _context.ReconciliationSourceProfiles
            .Where(p => p.IsActive && (p.AccountId == accountId || p.AccountId == null))
            // Específicos de la cuenta antes que globales: el primero que
            // matchee el payout gana.
            .OrderByDescending(p => p.AccountId != null)
            .ThenBy(p => p.SourceKey)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ReconciliationSourceProfile>> GetAllAsync(CancellationToken ct = default)
        => await _context.ReconciliationSourceProfiles
            .OrderBy(p => p.SourceKey)
            .ThenBy(p => p.AccountId)
            .ToListAsync(ct);

    public void Add(ReconciliationSourceProfile profile) => _context.ReconciliationSourceProfiles.Add(profile);
    public void Update(ReconciliationSourceProfile profile) => _context.ReconciliationSourceProfiles.Update(profile);
    public void Remove(ReconciliationSourceProfile profile) => _context.ReconciliationSourceProfiles.Remove(profile);
}
