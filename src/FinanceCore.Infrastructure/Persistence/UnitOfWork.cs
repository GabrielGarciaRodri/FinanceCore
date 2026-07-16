using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly FinanceCoreDbContext _context;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDailyBalanceRepository _dailyBalanceRepository;
    private readonly IExchangeRateRepository _exchangeRateRepository;
    private readonly IReconciliationRepository _reconciliationRepository;
    private readonly IReconciliationSourceProfileRepository _sourceProfileRepository;
    private readonly IAlertRuleRepository _alertRuleRepository;

    public UnitOfWork(
        FinanceCoreDbContext context,
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        IDailyBalanceRepository dailyBalanceRepository,
        IExchangeRateRepository exchangeRateRepository,
        IReconciliationRepository reconciliationRepository,
        IReconciliationSourceProfileRepository sourceProfileRepository,
        IAlertRuleRepository alertRuleRepository)
    {
        _context = context;
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _dailyBalanceRepository = dailyBalanceRepository;
        _exchangeRateRepository = exchangeRateRepository;
        _reconciliationRepository = reconciliationRepository;
        _sourceProfileRepository = sourceProfileRepository;
        _alertRuleRepository = alertRuleRepository;
    }

    public ITransactionRepository Transactions => _transactionRepository;
    public IAccountRepository Accounts => _accountRepository;
    public IDailyBalanceRepository DailyBalances => _dailyBalanceRepository;
    public IExchangeRateRepository ExchangeRates => _exchangeRateRepository;
    public IReconciliationRepository Reconciliations => _reconciliationRepository;
    public IReconciliationSourceProfileRepository SourceProfiles => _sourceProfileRepository;
    public IAlertRuleRepository AlertRules => _alertRuleRepository;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction == null)
            await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction != null)
            await _context.Database.CurrentTransaction.CommitAsync(cancellationToken);
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction != null)
            await _context.Database.CurrentTransaction.RollbackAsync(cancellationToken);
    }

    public void Dispose() => _context.Dispose();
}
