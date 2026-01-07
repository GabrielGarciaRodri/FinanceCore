using MediatR;

namespace FinanceCore.Domain.Events;

/// <summary>
/// Interfaz base para eventos de dominio.
/// </summary>
public interface IDomainEvent : INotification
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Clase base para eventos de dominio.
/// </summary>
public abstract record DomainEventBase : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Evento emitido cuando se crea una nueva transacción.
/// </summary>
public record TransactionCreatedEvent : DomainEventBase
{
    public Guid TransactionId { get; }
    public string ExternalId { get; }
    public Guid AccountId { get; }
    public decimal Amount { get; }
    public string CurrencyCode { get; }
    public DateOnly ValueDate { get; }

    public TransactionCreatedEvent(Entities.Transaction transaction)
    {
        TransactionId = transaction.Id;
        ExternalId = transaction.ExternalId;
        AccountId = transaction.AccountId;
        Amount = transaction.Amount.Amount;
        CurrencyCode = transaction.Amount.Currency.Code;
        ValueDate = transaction.ValueDate;
    }
}

/// <summary>
/// Evento emitido cuando una transacción es contabilizada.
/// </summary>
public record TransactionPostedEvent : DomainEventBase
{
    public Guid TransactionId { get; }
    public Guid AccountId { get; }
    public decimal Amount { get; }
    public string CurrencyCode { get; }
    public DateOnly ValueDate { get; }
    public DateTimeOffset PostedAt { get; }

    public TransactionPostedEvent(Entities.Transaction transaction)
    {
        TransactionId = transaction.Id;
        AccountId = transaction.AccountId;
        Amount = transaction.Amount.Amount;
        CurrencyCode = transaction.Amount.Currency.Code;
        ValueDate = transaction.ValueDate;
        PostedAt = transaction.ProcessedAt ?? DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Evento emitido cuando se completa una conciliación.
/// </summary>
public record ReconciliationCompletedEvent : DomainEventBase
{
    public Guid ReconciliationId { get; }
    public Guid AccountId { get; }
    public DateOnly ReconciliationDate { get; }
    public int MatchedCount { get; }
    public int UnmatchedCount { get; }
    public decimal DiscrepancyAmount { get; }
    public bool HasDiscrepancies { get; }

    public ReconciliationCompletedEvent(
        Guid reconciliationId,
        Guid accountId,
        DateOnly reconciliationDate,
        int matchedCount,
        int unmatchedCount,
        decimal discrepancyAmount)
    {
        ReconciliationId = reconciliationId;
        AccountId = accountId;
        ReconciliationDate = reconciliationDate;
        MatchedCount = matchedCount;
        UnmatchedCount = unmatchedCount;
        DiscrepancyAmount = discrepancyAmount;
        HasDiscrepancies = unmatchedCount > 0 || discrepancyAmount != 0;
    }
}

/// <summary>
/// Evento emitido cuando el saldo de una cuenta cambia significativamente.
/// </summary>
public record AccountBalanceChangedEvent : DomainEventBase
{
    public Guid AccountId { get; }
    public decimal PreviousBalance { get; }
    public decimal NewBalance { get; }
    public decimal Change { get; }
    public string CurrencyCode { get; }
    public Guid? TriggeringTransactionId { get; }

    public AccountBalanceChangedEvent(
        Guid accountId,
        decimal previousBalance,
        decimal newBalance,
        string currencyCode,
        Guid? triggeringTransactionId = null)
    {
        AccountId = accountId;
        PreviousBalance = previousBalance;
        NewBalance = newBalance;
        Change = newBalance - previousBalance;
        CurrencyCode = currencyCode;
        TriggeringTransactionId = triggeringTransactionId;
    }
}

/// <summary>
/// Evento emitido cuando se detecta una posible transacción duplicada.
/// </summary>
public record DuplicateTransactionDetectedEvent : DomainEventBase
{
    public Guid ExistingTransactionId { get; }
    public string ExternalId { get; }
    public string Source { get; }
    public string Hash { get; }

    public DuplicateTransactionDetectedEvent(
        Guid existingTransactionId,
        string externalId,
        string source,
        string hash)
    {
        ExistingTransactionId = existingTransactionId;
        ExternalId = externalId;
        Source = source;
        Hash = hash;
    }
}

/// <summary>
/// Evento emitido cuando un job ETL completa su ejecución.
/// </summary>
public record EtlJobCompletedEvent : DomainEventBase
{
    public string JobId { get; }
    public string JobType { get; }
    public int RecordsProcessed { get; }
    public int RecordsFailed { get; }
    public TimeSpan Duration { get; }
    public bool Success { get; }
    public string? ErrorMessage { get; }

    public EtlJobCompletedEvent(
        string jobId,
        string jobType,
        int recordsProcessed,
        int recordsFailed,
        TimeSpan duration,
        bool success,
        string? errorMessage = null)
    {
        JobId = jobId;
        JobType = jobType;
        RecordsProcessed = recordsProcessed;
        RecordsFailed = recordsFailed;
        Duration = duration;
        Success = success;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Evento emitido cuando se detecta una anomalía financiera.
/// </summary>
public record FinancialAnomalyDetectedEvent : DomainEventBase
{
    public string AnomalyType { get; }
    public string Description { get; }
    public Guid? AccountId { get; }
    public Guid? TransactionId { get; }
    public decimal? Amount { get; }
    public string Severity { get; } // "Low", "Medium", "High", "Critical"
    public Dictionary<string, object>? AdditionalData { get; }

    public FinancialAnomalyDetectedEvent(
        string anomalyType,
        string description,
        string severity,
        Guid? accountId = null,
        Guid? transactionId = null,
        decimal? amount = null,
        Dictionary<string, object>? additionalData = null)
    {
        AnomalyType = anomalyType;
        Description = description;
        Severity = severity;
        AccountId = accountId;
        TransactionId = transactionId;
        Amount = amount;
        AdditionalData = additionalData;
    }
}
