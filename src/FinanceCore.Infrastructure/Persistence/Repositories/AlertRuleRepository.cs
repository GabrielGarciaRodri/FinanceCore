using Microsoft.EntityFrameworkCore;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Persistence.Repositories;

public class AlertRuleRepository : IAlertRuleRepository
{
    private readonly FinanceCoreDbContext _context;

    public AlertRuleRepository(FinanceCoreDbContext context)
    {
        _context = context;
    }

    public async Task<AlertRule?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.AlertRules.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<AlertRule>> GetAllAsync(CancellationToken ct = default)
        => await _context.AlertRules
            .OrderBy(r => r.Type)
            .ThenBy(r => r.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AlertRule>> GetEnabledByTypeAsync(
        AlertRuleType type,
        CancellationToken ct = default)
        => await _context.AlertRules
            .Where(r => r.IsEnabled && r.Type == type)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    public void Add(AlertRule rule) => _context.AlertRules.Add(rule);
    public void Update(AlertRule rule) => _context.AlertRules.Update(rule);
    public void Remove(AlertRule rule) => _context.AlertRules.Remove(rule);
}
