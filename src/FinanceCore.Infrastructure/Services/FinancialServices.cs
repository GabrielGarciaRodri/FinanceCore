using Microsoft.Extensions.Logging;
using FinanceCore.Application.Transactions.Commands.IngestTransactions;
using Microsoft.EntityFrameworkCore;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;
using FinanceCore.Domain.ValueObjects;
using FinanceCore.Infrastructure.Persistence.Context;
using FinanceCore.Infrastructure.BackgroundJobs.Jobs;

namespace FinanceCore.Infrastructure.Services;

#region Unit of Work

public class UnitOfWork : IUnitOfWork
{
    private readonly FinanceCoreDbContext _context;
    private readonly ILoggerFactory _loggerFactory;
    private ITransactionRepository? _transactionRepository;
    private IAccountRepository? _accountRepository;
    private IDailyBalanceRepository? _dailyBalanceRepository;
    private IExchangeRateRepository? _exchangeRateRepository;

    public UnitOfWork(FinanceCoreDbContext context, ILoggerFactory loggerFactory)
    {
        _context = context;
        _loggerFactory = loggerFactory;
    }

    public ITransactionRepository Transactions => 
        _transactionRepository ??= new Persistence.Repositories.TransactionRepository(
            _context, 
            _loggerFactory.CreateLogger<Persistence.Repositories.TransactionRepository>());

    public IAccountRepository Accounts => 
        _accountRepository ??= new AccountRepository(_context);

    public IDailyBalanceRepository DailyBalances => 
        _dailyBalanceRepository ??= new DailyBalanceRepository(_context);

    public IExchangeRateRepository ExchangeRates => 
        _exchangeRateRepository ??= new ExchangeRateRepository(_context);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        => await _context.Database.BeginTransactionAsync(cancellationToken);

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

#endregion

#region Account Repository

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
        var balances = await _context.Accounts
            .Where(a => a.IsActive)
            .GroupBy(a => a.Currency.Code)
            .Select(g => new { Currency = g.Key, Total = g.Sum(a => (decimal)a.CurrentBalance.Amount) })
            .ToListAsync(ct);
        
        return balances.ToDictionary(b => b.Currency, b => b.Total);
    }
}

#endregion

#region DailyBalance Repository

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
        => await Task.FromResult<IReadOnlyList<DailyBalance>>(new List<DailyBalance>());

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

#endregion

#region ExchangeRate Repository

public class ExchangeRateRepository : IExchangeRateRepository
{
    private readonly FinanceCoreDbContext _context;

    public ExchangeRateRepository(FinanceCoreDbContext context) => _context = context;

    public async Task<ExchangeRate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.ExchangeRates.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<ExchangeRate>> GetAllAsync(
        System.Linq.Expressions.Expression<Func<ExchangeRate, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.ExchangeRates.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.ToListAsync(ct);
    }

    public void Add(ExchangeRate entity) => _context.ExchangeRates.Add(entity);
    public void AddRange(IEnumerable<ExchangeRate> entities) => _context.ExchangeRates.AddRange(entities);
    public void Update(ExchangeRate entity) => _context.ExchangeRates.Update(entity);
    public void Remove(ExchangeRate entity) => _context.ExchangeRates.Remove(entity);

    public async Task<bool> ExistsAsync(
        System.Linq.Expressions.Expression<Func<ExchangeRate, bool>> predicate,
        CancellationToken ct = default)
        => await _context.ExchangeRates.AnyAsync(predicate, ct);

    public async Task<int> CountAsync(
        System.Linq.Expressions.Expression<Func<ExchangeRate, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.ExchangeRates.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.CountAsync(ct);
    }

    public async Task<ExchangeRate?> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default)
        => await _context.ExchangeRates
            .Where(e => e.FromCurrency == from && e.ToCurrency == to && e.EffectiveDate <= date)
            .OrderByDescending(e => e.EffectiveDate)
            .FirstOrDefaultAsync(ct);

    public async Task<ExchangeRate?> GetLatestRateAsync(string from, string to, CancellationToken ct = default)
        => await _context.ExchangeRates
            .Where(e => e.FromCurrency == from && e.ToCurrency == to)
            .OrderByDescending(e => e.EffectiveDate)
            .FirstOrDefaultAsync(ct);

    public async Task<decimal> ConvertAsync(decimal amount, string from, string to, DateOnly date, CancellationToken ct = default)
    {
        if (from == to) return amount;
        var rate = await GetRateAsync(from, to, date, ct);
        if (rate == null) throw new InvalidOperationException($"Exchange rate not found: {from} to {to}");
        return Math.Round(amount * rate.Rate, 4, MidpointRounding.ToEven);
    }

    public async Task<IReadOnlyList<ExchangeRate>> GetHistoricalRatesAsync(
        string from, string to, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
        => await _context.ExchangeRates
            .Where(e => e.FromCurrency == from && e.ToCurrency == to && e.EffectiveDate >= startDate && e.EffectiveDate <= endDate)
            .OrderBy(e => e.EffectiveDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExchangeRate>> GetAllLatestRatesAsync(string baseCurrency, CancellationToken ct = default)
        => await _context.ExchangeRates
            .Where(e => e.FromCurrency == baseCurrency)
            .GroupBy(e => e.ToCurrency)
            .Select(g => g.OrderByDescending(e => e.EffectiveDate).First())
            .ToListAsync(ct);

    public async Task AddAsync(ExchangeRate rate, CancellationToken ct = default)
        => await _context.ExchangeRates.AddAsync(rate, ct);

    public async Task AddRangeAsync(IEnumerable<ExchangeRate> rates, CancellationToken ct = default)
        => await _context.ExchangeRates.AddRangeAsync(rates, ct);

    public async Task<IReadOnlyList<ExchangeRate>> GetHistoryAsync(
        string from, string to, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
        => await GetHistoricalRatesAsync(from, to, startDate, endDate, ct);
}

#endregion

#region File Ingestion Service

public class FileIngestionService : IFileIngestionService
{
    private readonly ILogger<FileIngestionService> _logger;

    public FileIngestionService(ILogger<FileIngestionService> logger) => _logger = logger;

    public Task<IReadOnlyList<PendingFile>> GetPendingFilesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Checking for pending files...");
        return Task.FromResult<IReadOnlyList<PendingFile>>(new List<PendingFile>());
    }

    public Task<IReadOnlyList<TransactionDto>> ParseCsvAsync(PendingFile file, CancellationToken ct = default)
    {
        _logger.LogInformation("Parsing CSV: {FilePath}", file.FullPath);
        return Task.FromResult<IReadOnlyList<TransactionDto>>(new List<TransactionDto>());
    }

    public Task<IReadOnlyList<TransactionDto>> ParseExcelAsync(PendingFile file, CancellationToken ct = default)
    {
        _logger.LogInformation("Parsing Excel: {FilePath}", file.FullPath);
        return Task.FromResult<IReadOnlyList<TransactionDto>>(new List<TransactionDto>());
    }

    public Task MoveToProcessedAsync(PendingFile file, CancellationToken ct = default)
    {
        _logger.LogInformation("Moving to processed: {FilePath}", file.FullPath);
        return Task.CompletedTask;
    }

    public Task MoveToErrorAsync(PendingFile file, string error, CancellationToken ct = default)
    {
        _logger.LogWarning("Moving to error: {FilePath}, Reason: {Error}", file.FullPath, error);
        return Task.CompletedTask;
    }
}

#endregion

#region Exchange Rate Provider

public class ExchangeRateProvider : IExchangeRateProvider
{
    private readonly ILogger<ExchangeRateProvider> _logger;

    public ExchangeRateProvider(ILogger<ExchangeRateProvider> logger) => _logger = logger;

    public string ProviderName => "MockProvider";

    public Task<IEnumerable<ExchangeRateData>> GetLatestRatesAsync(
        string baseCurrency, string[] targetCurrencies, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching rates for {Base}", baseCurrency);
        
        var rates = targetCurrencies.Select(target => 
            new ExchangeRateData(baseCurrency, target, GetMockRate(baseCurrency, target)));

        return Task.FromResult(rates);
    }

    private static decimal GetMockRate(string from, string to) => (from, to) switch
    {
        ("USD", "COP") => 4150.00m,
        ("USD", "EUR") => 0.92m,
        ("USD", "MXN") => 17.15m,
        ("USD", "BRL") => 4.95m,
        ("EUR", "USD") => 1.09m,
        ("COP", "USD") => 0.00024m,
        _ => 1.00m
    };
}

#endregion

#region Reconciliation Engine

public class ReconciliationEngine : IReconciliationEngine
{
    private readonly ILogger<ReconciliationEngine> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public ReconciliationEngine(ILogger<ReconciliationEngine> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public async Task<ReconciliationResult> ReconcileAsync(Guid accountId, DateOnly date, CancellationToken ct = default)
    {
        _logger.LogInformation("Reconciling account {AccountId} for {Date}", accountId, date);

        var transactions = await _unitOfWork.Transactions.GetByAccountAndDateRangeAsync(accountId, date, date, ct);

        return new ReconciliationResult(
            MatchedCount: transactions.Count,
            UnmatchedCount: 0,
            DiscrepancyAmount: 0m,
            HasDiscrepancies: false
        );
    }
}

#endregion
