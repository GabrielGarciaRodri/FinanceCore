using System.Linq.Expressions;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.ValueObjects;

namespace FinanceCore.Domain.Repositories;

/// <summary>
/// Interfaz base para repositorios.
/// </summary>
/// <typeparam name="T">Tipo de entidad</typeparam>
public interface IRepository<T> where T : BaseEntity, IAggregateRoot
{
    /// <summary>
    /// Obtiene una entidad por su ID.
    /// </summary>
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Obtiene todas las entidades que cumplen una condición.
    /// </summary>
    Task<IReadOnlyList<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Agrega una nueva entidad.
    /// </summary>
    void Add(T entity);
    
    /// <summary>
    /// Agrega múltiples entidades.
    /// </summary>
    void AddRange(IEnumerable<T> entities);
    
    /// <summary>
    /// Actualiza una entidad existente.
    /// </summary>
    void Update(T entity);
    
    /// <summary>
    /// Elimina una entidad.
    /// </summary>
    void Remove(T entity);
    
    /// <summary>
    /// Verifica si existe alguna entidad que cumpla la condición.
    /// </summary>
    Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cuenta las entidades que cumplen una condición.
    /// </summary>
    Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Repositorio especializado para transacciones.
/// </summary>
public interface ITransactionRepository : IRepository<Transaction>
{
    /// <summary>
    /// Busca una transacción por su ID externo y fuente.
    /// CRÍTICO para idempotencia.
    /// </summary>
    Task<Transaction?> GetByExternalIdAsync(
        string externalId, 
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca transacciones por hash (para detección de duplicados).
    /// </summary>
    Task<Transaction?> GetByHashAsync(
        string hash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene transacciones de una cuenta en un rango de fechas.
    /// </summary>
    Task<IReadOnlyList<Transaction>> GetByAccountAndDateRangeAsync(
        Guid accountId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene transacciones pendientes de procesar.
    /// </summary>
    Task<IReadOnlyList<Transaction>> GetPendingTransactionsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene transacciones pendientes de conciliación para una cuenta.
    /// </summary>
    Task<IReadOnlyList<Transaction>> GetUnreconciledByAccountAsync(
        Guid accountId,
        DateOnly? beforeDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el resumen de transacciones por cuenta y período.
    /// </summary>
    Task<TransactionSummary> GetSummaryAsync(
        Guid accountId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca transacciones con criterios avanzados.
    /// </summary>
    Task<PagedResult<Transaction>> SearchAsync(
        TransactionSearchCriteria criteria,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Repositorio especializado para cuentas.
/// </summary>
public interface IAccountRepository : IRepository<Account>
{
    /// <summary>
    /// Busca una cuenta por número y institución.
    /// </summary>
    Task<Account?> GetByAccountNumberAsync(
        string accountNumber,
        Guid institutionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene todas las cuentas activas.
    /// </summary>
    Task<IReadOnlyList<Account>> GetActiveAccountsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene cuentas por institución.
    /// </summary>
    Task<IReadOnlyList<Account>> GetByInstitutionAsync(
        Guid institutionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene una cuenta con bloqueo optimista.
    /// </summary>
    Task<Account?> GetByIdWithLockAsync(
        Guid id,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el balance total por moneda.
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetTotalBalancesByCurrencyAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Repositorio para balances diarios.
/// </summary>
public interface IDailyBalanceRepository
{
    /// <summary>
    /// Obtiene el balance de una cuenta en una fecha específica.
    /// </summary>
    Task<DailyBalance?> GetByAccountAndDateAsync(
        Guid accountId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el último balance registrado de una cuenta.
    /// </summary>
    Task<DailyBalance?> GetLatestByAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene balances de una cuenta en un rango de fechas.
    /// </summary>
    Task<IReadOnlyList<DailyBalance>> GetByAccountAndDateRangeAsync(
        Guid accountId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrega o actualiza un balance diario.
    /// </summary>
    Task UpsertAsync(DailyBalance balance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene balances pendientes de conciliación.
    /// </summary>
    Task<IReadOnlyList<DailyBalance>> GetUnreconciledAsync(
        DateOnly? beforeDate = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Repositorio para tipos de cambio.
/// </summary>
public interface IExchangeRateRepository
{
    /// <summary>
    /// Obtiene el tipo de cambio para una fecha específica.
    /// </summary>
    Task<ExchangeRate?> GetRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el último tipo de cambio disponible.
    /// </summary>
    Task<ExchangeRate?> GetLatestRateAsync(
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrega un nuevo tipo de cambio.
    /// </summary>
    Task AddAsync(ExchangeRate rate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrega múltiples tipos de cambio.
    /// </summary>
    Task AddRangeAsync(IEnumerable<ExchangeRate> rates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene historial de tipos de cambio.
    /// </summary>
    Task<IReadOnlyList<ExchangeRate>> GetHistoryAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Unit of Work para coordinar transacciones de base de datos.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    ITransactionRepository Transactions { get; }
    IAccountRepository Accounts { get; }
    IDailyBalanceRepository DailyBalances { get; }
    IExchangeRateRepository ExchangeRates { get; }
    
    /// <summary>
    /// Guarda todos los cambios pendientes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Inicia una transacción de base de datos.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Confirma la transacción actual.
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Revierte la transacción actual.
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}

#region Supporting Types

/// <summary>
/// Resultado paginado genérico.
/// </summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

/// <summary>
/// Criterios de búsqueda para transacciones.
/// </summary>
public class TransactionSearchCriteria
{
    public Guid? AccountId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public TransactionType? Type { get; set; }
    public TransactionStatus? Status { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public string? Category { get; set; }
    public string? SearchText { get; set; }
    public IEnumerable<string>? Tags { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
}

/// <summary>
/// Resumen de transacciones.
/// </summary>
public class TransactionSummary
{
    public Guid AccountId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public int TotalCount { get; init; }
    public decimal TotalDebits { get; init; }
    public decimal TotalCredits { get; init; }
    public decimal NetChange => TotalCredits + TotalDebits; // Débitos son negativos
    public decimal AverageTransactionAmount { get; init; }
    public decimal LargestDebit { get; init; }
    public decimal LargestCredit { get; init; }
    public Dictionary<string, int> CountByCategory { get; init; } = new();
    public Dictionary<TransactionStatus, int> CountByStatus { get; init; } = new();
}

#endregion

