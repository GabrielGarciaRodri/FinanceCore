using System.Linq.Expressions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de transacciones.
/// Usa EF Core para escrituras y Dapper para lecturas complejas.
/// </summary>
public class TransactionRepository : ITransactionRepository
{
    private readonly FinanceCoreDbContext _context;
    private readonly ILogger<TransactionRepository> _logger;

    public TransactionRepository(
        FinanceCoreDbContext context,
        ILogger<TransactionRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region IRepository Implementation (EF Core)

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .Include(t => t.Entries)
            .Include(t => t.Source)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> GetAllAsync(
        Expression<Func<Transaction, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Transactions.AsQueryable();
        
        if (predicate != null)
            query = query.Where(predicate);
        
        return await query.ToListAsync(cancellationToken);
    }

    public void Add(Transaction entity)
    {
        _context.Transactions.Add(entity);
    }

    public void AddRange(IEnumerable<Transaction> entities)
    {
        _context.Transactions.AddRange(entities);
    }

    public void Update(Transaction entity)
    {
        _context.Transactions.Update(entity);
    }

    public void Remove(Transaction entity)
    {
        _context.Transactions.Remove(entity);
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<Transaction, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _context.Transactions.AnyAsync(predicate, cancellationToken);
    }

    public async Task<int> CountAsync(
        Expression<Func<Transaction, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Transactions.AsQueryable();
        
        if (predicate != null)
            query = query.Where(predicate);
        
        return await query.CountAsync(cancellationToken);
    }

    #endregion

    #region Specialized Methods

    public async Task<Transaction?> GetByExternalIdAsync(
        string externalId,
        string source,
        CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => 
                t.ExternalId == externalId && 
                t.ExternalIdSource == source,
                cancellationToken);
    }

    public async Task<Transaction?> GetByHashAsync(
        string hash,
        CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Hash == hash, cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> GetByAccountAndDateRangeAsync(
        Guid accountId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .Where(t => t.AccountId == accountId &&
                       t.ValueDate >= startDate &&
                       t.ValueDate <= endDate)
            .OrderBy(t => t.ValueDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> GetPendingTransactionsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .Where(t => t.Status == TransactionStatus.Pending)
            .OrderBy(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> GetUnreconciledByAccountAsync(
        Guid accountId,
        DateOnly? beforeDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Transactions
            .Where(t => t.AccountId == accountId &&
                       t.ReconciliationId == null &&
                       t.Status == TransactionStatus.Posted);

        if (beforeDate.HasValue)
            query = query.Where(t => t.ValueDate < beforeDate.Value);

        return await query
            .OrderBy(t => t.ValueDate)
            .ToListAsync(cancellationToken);
    }

    #endregion

    #region Dapper Queries (High Performance)

    public async Task<TransactionSummary> GetSummaryAsync(
        Guid accountId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                @AccountId AS AccountId,
                @StartDate AS StartDate,
                @EndDate AS EndDate,
                COUNT(*) AS TotalCount,
                COALESCE(SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END), 0) AS TotalDebits,
                COALESCE(SUM(CASE WHEN amount > 0 THEN amount ELSE 0 END), 0) AS TotalCredits,
                COALESCE(AVG(ABS(amount)), 0) AS AverageTransactionAmount,
                COALESCE(MIN(CASE WHEN amount < 0 THEN amount END), 0) AS LargestDebit,
                COALESCE(MAX(CASE WHEN amount > 0 THEN amount END), 0) AS LargestCredit
            FROM transactions t
            WHERE t.account_id = @AccountId
              AND t.value_date >= @StartDate
              AND t.value_date <= @EndDate
              AND t.status NOT IN (6, 7);
        ";

        await using var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        var summary = await connection.QueryFirstOrDefaultAsync<TransactionSummary>(sql, new
        {
            AccountId = accountId,
            StartDate = startDate,
            EndDate = endDate
        });

        return summary ?? new TransactionSummary
        {
            AccountId = accountId,
            StartDate = startDate,
            EndDate = endDate
        };
    }

    public async Task<PagedResult<Transaction>> SearchAsync(
        TransactionSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Transactions.AsQueryable();

        if (criteria.AccountId.HasValue)
            query = query.Where(t => t.AccountId == criteria.AccountId.Value);

        if (criteria.StartDate.HasValue)
            query = query.Where(t => t.ValueDate >= criteria.StartDate.Value);

        if (criteria.EndDate.HasValue)
            query = query.Where(t => t.ValueDate <= criteria.EndDate.Value);

        if (criteria.Type.HasValue)
            query = query.Where(t => t.Type == criteria.Type.Value);

        if (criteria.Status.HasValue)
            query = query.Where(t => t.Status == criteria.Status.Value);

        if (!string.IsNullOrWhiteSpace(criteria.Category))
            query = query.Where(t => t.Category == criteria.Category);

        if (!string.IsNullOrWhiteSpace(criteria.SearchText))
            query = query.Where(t => t.Description != null && 
                                     t.Description.Contains(criteria.SearchText));

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.ValueDate)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Transaction>
        {
            Items = items,
            TotalCount = totalCount,
            Page = criteria.Page,
            PageSize = criteria.PageSize
        };
    }

    #endregion
}
