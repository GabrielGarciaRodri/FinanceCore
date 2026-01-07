namespace FinanceCore.Domain.Exceptions;

/// <summary>
/// Excepción base para errores de dominio.
/// </summary>
public class DomainException : Exception
{
    public string? ErrorCode { get; }
    public IDictionary<string, object>? Details { get; }

    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DomainException(string message, string errorCode, IDictionary<string, object> details) 
        : base(message)
    {
        ErrorCode = errorCode;
        Details = details;
    }
}

/// <summary>
/// Excepción cuando no hay fondos suficientes para una operación.
/// </summary>
public class InsufficientFundsException : DomainException
{
    public Guid AccountId { get; }
    public decimal RequestedAmount { get; }
    public decimal AvailableAmount { get; }

    public InsufficientFundsException(Guid accountId, decimal requestedAmount, decimal availableAmount)
        : base($"Fondos insuficientes. Solicitado: {requestedAmount:N2}, Disponible: {availableAmount:N2}")
    {
        AccountId = accountId;
        RequestedAmount = requestedAmount;
        AvailableAmount = availableAmount;
    }
}

/// <summary>
/// Excepción durante el proceso de conciliación.
/// </summary>
public class ReconciliationException : DomainException
{
    public Guid? ReconciliationId { get; }
    public Guid? AccountId { get; }
    public DateOnly? ReconciliationDate { get; }

    public ReconciliationException(string message) : base(message)
    {
    }

    public ReconciliationException(string message, Guid reconciliationId) : base(message)
    {
        ReconciliationId = reconciliationId;
    }

    public ReconciliationException(string message, Guid accountId, DateOnly reconciliationDate) 
        : base(message)
    {
        AccountId = accountId;
        ReconciliationDate = reconciliationDate;
    }
}

/// <summary>
/// Excepción cuando se detecta una transacción duplicada.
/// </summary>
public class DuplicateTransactionException : DomainException
{
    public string ExternalId { get; }
    public string Source { get; }
    public Guid? ExistingTransactionId { get; }

    public DuplicateTransactionException(string externalId, string source, Guid? existingId = null)
        : base($"Transacción duplicada detectada. ExternalId: {externalId}, Source: {source}")
    {
        ExternalId = externalId;
        Source = source;
        ExistingTransactionId = existingId;
    }
}

/// <summary>
/// Excepción cuando los datos de una transacción no pasan validación.
/// </summary>
public class TransactionValidationException : DomainException
{
    public IReadOnlyList<string> ValidationErrors { get; }

    public TransactionValidationException(IEnumerable<string> errors)
        : base("La transacción no pasó las validaciones requeridas.")
    {
        ValidationErrors = errors.ToList().AsReadOnly();
    }

    public TransactionValidationException(string error)
        : this(new[] { error })
    {
    }
}

/// <summary>
/// Excepción cuando falla la conversión de moneda.
/// </summary>
public class CurrencyConversionException : DomainException
{
    public string FromCurrency { get; }
    public string ToCurrency { get; }
    public DateOnly Date { get; }

    public CurrencyConversionException(string fromCurrency, string toCurrency, DateOnly date)
        : base($"No se encontró tipo de cambio para {fromCurrency} -> {toCurrency} en fecha {date:yyyy-MM-dd}")
    {
        FromCurrency = fromCurrency;
        ToCurrency = toCurrency;
        Date = date;
    }
}

/// <summary>
/// Excepción cuando hay un error de integridad de datos.
/// </summary>
public class DataIntegrityException : DomainException
{
    public string EntityType { get; }
    public Guid? EntityId { get; }

    public DataIntegrityException(string message, string entityType, Guid? entityId = null)
        : base(message)
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}

/// <summary>
/// Excepción cuando la partida doble no cuadra.
/// </summary>
public class DoubleEntryException : DomainException
{
    public Guid TransactionId { get; }
    public decimal TotalDebits { get; }
    public decimal TotalCredits { get; }
    public decimal Difference => TotalDebits - TotalCredits;

    public DoubleEntryException(Guid transactionId, decimal totalDebits, decimal totalCredits)
        : base($"La transacción no cumple partida doble. " +
               $"Débitos: {totalDebits:N4}, Créditos: {totalCredits:N4}, " +
               $"Diferencia: {totalDebits - totalCredits:N4}")
    {
        TransactionId = transactionId;
        TotalDebits = totalDebits;
        TotalCredits = totalCredits;
    }
}

/// <summary>
/// Excepción cuando se intenta una transición de estado inválida.
/// </summary>
public class InvalidStateTransitionException : DomainException
{
    public string CurrentState { get; }
    public string AttemptedState { get; }
    public string EntityType { get; }
    public Guid EntityId { get; }

    public InvalidStateTransitionException(
        string entityType, 
        Guid entityId, 
        string currentState, 
        string attemptedState)
        : base($"Transición de estado inválida en {entityType} ({entityId}): " +
               $"{currentState} -> {attemptedState}")
    {
        EntityType = entityType;
        EntityId = entityId;
        CurrentState = currentState;
        AttemptedState = attemptedState;
    }
}
