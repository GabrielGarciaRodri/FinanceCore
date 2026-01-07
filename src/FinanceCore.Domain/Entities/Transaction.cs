using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Exceptions;
using FinanceCore.Domain.ValueObjects;
using FinanceCore.Domain.Events;

namespace FinanceCore.Domain.Entities;

/// <summary>
/// Entidad principal que representa una transacción financiera.
/// Implementa el patrón Rich Domain Model con toda la lógica de negocio encapsulada.
/// </summary>
public class Transaction : BaseEntity, IAggregateRoot
{
    #region Properties

    /// <summary>
    /// Identificador externo de la transacción (del sistema origen).
    /// CRÍTICO para idempotencia - combinado con ExternalIdSource debe ser único.
    /// </summary>
    public string ExternalId { get; private set; } = null!;
    
    /// <summary>
    /// Sistema origen del ID externo (ej: "BANCOLOMBIA_API", "PSE", "CSV_IMPORT").
    /// </summary>
    public string ExternalIdSource { get; private set; } = null!;

    /// <summary>
    /// Cuenta financiera asociada.
    /// </summary>
    public Guid AccountId { get; private set; }
    
    /// <summary>
    /// Tipo de transacción.
    /// </summary>
    public TransactionType Type { get; private set; }
    
    /// <summary>
    /// Estado actual de la transacción.
    /// </summary>
    public TransactionStatus Status { get; private set; }

    /// <summary>
    /// Monto de la transacción en la moneda de la cuenta.
    /// SIEMPRE negativo para débitos, positivo para créditos.
    /// </summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>
    /// Monto original si hubo conversión de moneda.
    /// </summary>
    public Money? OriginalAmount { get; private set; }
    
    /// <summary>
    /// Tasa de cambio usada si hubo conversión.
    /// </summary>
    public decimal? ExchangeRateUsed { get; private set; }
    
    /// <summary>
    /// ID del registro de tipo de cambio usado.
    /// </summary>
    public Guid? ExchangeRateId { get; private set; }

    /// <summary>
    /// Fecha valor - cuando la transacción afecta saldos.
    /// </summary>
    public DateOnly ValueDate { get; private set; }
    
    /// <summary>
    /// Fecha contable - cuando se registra en libros.
    /// </summary>
    public DateOnly BookingDate { get; private set; }
    
    /// <summary>
    /// Momento exacto de ejecución (si está disponible).
    /// </summary>
    public DateTimeOffset? ExecutionDate { get; private set; }

    /// <summary>
    /// Descripción de la transacción.
    /// </summary>
    public string? Description { get; private set; }
    
    /// <summary>
    /// Categoría principal.
    /// </summary>
    public string? Category { get; private set; }
    
    /// <summary>
    /// Subcategoría.
    /// </summary>
    public string? SubCategory { get; private set; }

    /// <summary>
    /// Información de la contraparte.
    /// </summary>
    public CounterpartyInfo? Counterparty { get; private set; }

    /// <summary>
    /// ID de conciliación (se llena cuando se concilia).
    /// </summary>
    public Guid? ReconciliationId { get; private set; }
    
    /// <summary>
    /// Fecha/hora de conciliación.
    /// </summary>
    public DateTimeOffset? ReconciledAt { get; private set; }

    /// <summary>
    /// Hash SHA-256 para detección de duplicados.
    /// Calculado a partir de: ExternalId + Amount + ValueDate + AccountId
    /// </summary>
    public string Hash { get; private set; } = null!;
    
    /// <summary>
    /// Checksum de integridad.
    /// </summary>
    public string? Checksum { get; private set; }

    /// <summary>
    /// Metadatos adicionales específicos del origen.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; private set; }
    
    /// <summary>
    /// Etiquetas para búsqueda y filtrado.
    /// </summary>
    public List<string> Tags { get; private set; } = new();

    /// <summary>
    /// Fecha/hora de procesamiento.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    // Navigation properties (para EF Core)
    public virtual Account? Account { get; private set; }
    public virtual ICollection<FinancialEntry> Entries { get; private set; } = new List<FinancialEntry>();
    public virtual TransactionSource? Source { get; private set; }

    #endregion

    #region Constructors

    /// <summary>
    /// Constructor privado para EF Core.
    /// </summary>
    private Transaction() { }

    /// <summary>
    /// Crea una nueva transacción con validaciones de dominio.
    /// </summary>
    private Transaction(
        string externalId,
        string externalIdSource,
        Guid accountId,
        TransactionType type,
        Money amount,
        DateOnly valueDate,
        DateOnly bookingDate,
        string? description = null)
    {
        // Validaciones
        if (string.IsNullOrWhiteSpace(externalId))
            throw new DomainException("El ID externo es requerido.");
            
        if (string.IsNullOrWhiteSpace(externalIdSource))
            throw new DomainException("El origen del ID externo es requerido.");
            
        if (accountId == Guid.Empty)
            throw new DomainException("El ID de cuenta es requerido.");

        // Validar que el signo del monto coincide con el tipo
        ValidateAmountSign(type, amount);

        // Validar fechas
        if (bookingDate < valueDate.AddDays(-5))
            throw new DomainException("La fecha de booking no puede ser más de 5 días antes de la fecha valor.");

        // Asignar valores
        Id = Guid.NewGuid();
        ExternalId = externalId.Trim();
        ExternalIdSource = externalIdSource.Trim().ToUpperInvariant();
        AccountId = accountId;
        Type = type;
        Status = TransactionStatus.Pending;
        Amount = amount;
        ValueDate = valueDate;
        BookingDate = bookingDate;
        Description = description?.Trim();
        CreatedAt = DateTimeOffset.UtcNow;
        
        // Calcular hash para detección de duplicados
        Hash = CalculateHash();
        
        // Agregar evento de dominio
        AddDomainEvent(new TransactionCreatedEvent(this));
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Crea una transacción de débito (salida de fondos).
    /// </summary>
    public static Transaction CreateDebit(
        string externalId,
        string source,
        Guid accountId,
        decimal amount,
        string currencyCode,
        DateOnly valueDate,
        DateOnly? bookingDate = null,
        string? description = null)
    {
        // Los débitos SIEMPRE son negativos
        var absoluteAmount = Math.Abs(amount);
        var money = Money.Create(-absoluteAmount, currencyCode);
        
        return new Transaction(
            externalId,
            source,
            accountId,
            TransactionType.Debit,
            money,
            valueDate,
            bookingDate ?? valueDate,
            description);
    }

    /// <summary>
    /// Crea una transacción de crédito (entrada de fondos).
    /// </summary>
    public static Transaction CreateCredit(
        string externalId,
        string source,
        Guid accountId,
        decimal amount,
        string currencyCode,
        DateOnly valueDate,
        DateOnly? bookingDate = null,
        string? description = null)
    {
        // Los créditos SIEMPRE son positivos
        var absoluteAmount = Math.Abs(amount);
        var money = Money.Create(absoluteAmount, currencyCode);
        
        return new Transaction(
            externalId,
            source,
            accountId,
            TransactionType.Credit,
            money,
            valueDate,
            bookingDate ?? valueDate,
            description);
    }

    /// <summary>
    /// Crea una transacción de transferencia.
    /// </summary>
    public static Transaction CreateTransfer(
        string externalId,
        string source,
        Guid accountId,
        decimal amount,
        string currencyCode,
        DateOnly valueDate,
        bool isOutgoing,
        CounterpartyInfo counterparty,
        string? description = null)
    {
        var type = isOutgoing ? TransactionType.TransferOut : TransactionType.TransferIn;
        var signedAmount = isOutgoing ? -Math.Abs(amount) : Math.Abs(amount);
        var money = Money.Create(signedAmount, currencyCode);
        
        var transaction = new Transaction(
            externalId,
            source,
            accountId,
            type,
            money,
            valueDate,
            valueDate,
            description);

        transaction.Counterparty = counterparty;
        
        return transaction;
    }

    /// <summary>
    /// Crea una transacción de comisión bancaria.
    /// </summary>
    public static Transaction CreateFee(
        string externalId,
        string source,
        Guid accountId,
        decimal amount,
        string currencyCode,
        DateOnly valueDate,
        string description)
    {
        // Las comisiones SIEMPRE son débitos (negativos)
        var money = Money.Create(-Math.Abs(amount), currencyCode);
        
        return new Transaction(
            externalId,
            source,
            accountId,
            TransactionType.Fee,
            money,
            valueDate,
            valueDate,
            description);
    }

    /// <summary>
    /// Crea un ajuste contable.
    /// </summary>
    public static Transaction CreateAdjustment(
        string externalId,
        Guid accountId,
        decimal amount,
        string currencyCode,
        DateOnly valueDate,
        string description,
        string reason)
    {
        var money = Money.Create(amount, currencyCode); // Puede ser positivo o negativo
        
        var transaction = new Transaction(
            externalId,
            "SYSTEM_ADJUSTMENT",
            accountId,
            TransactionType.Adjustment,
            money,
            valueDate,
            valueDate,
            description);

        transaction.Metadata = new Dictionary<string, object>
        {
            ["AdjustmentReason"] = reason,
            ["CreatedBySystem"] = true
        };
        
        return transaction;
    }

    #endregion

    #region State Transitions

    /// <summary>
    /// Marca la transacción como en procesamiento.
    /// </summary>
    public void StartProcessing()
    {
        EnsureCanTransition(TransactionStatus.Processing);
        Status = TransactionStatus.Processing;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marca la transacción como validada.
    /// </summary>
    public void MarkAsValidated()
    {
        EnsureCanTransition(TransactionStatus.Validated);
        Status = TransactionStatus.Validated;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Contabiliza la transacción (afecta saldos).
    /// </summary>
    public void Post()
    {
        if (Status != TransactionStatus.Validated)
            throw new DomainException($"Solo se pueden contabilizar transacciones validadas. Estado actual: {Status}");

        Status = TransactionStatus.Posted;
        ProcessedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        
        AddDomainEvent(new TransactionPostedEvent(this));
    }

    /// <summary>
    /// Marca la transacción como conciliada.
    /// </summary>
    public void Reconcile(Guid reconciliationId)
    {
        if (Status != TransactionStatus.Posted)
            throw new DomainException($"Solo se pueden conciliar transacciones contabilizadas. Estado actual: {Status}");

        if (reconciliationId == Guid.Empty)
            throw new DomainException("El ID de conciliación es requerido.");

        Status = TransactionStatus.Reconciled;
        ReconciliationId = reconciliationId;
        ReconciledAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Rechaza la transacción.
    /// </summary>
    public void Reject(string reason)
    {
        if (Status.IsFinalState())
            throw new DomainException($"No se puede rechazar una transacción en estado final: {Status}");

        Status = TransactionStatus.Rejected;
        UpdatedAt = DateTimeOffset.UtcNow;
        
        Metadata ??= new Dictionary<string, object>();
        Metadata["RejectionReason"] = reason;
        Metadata["RejectedAt"] = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marca la transacción como reversada.
    /// </summary>
    public void MarkAsReversed(Guid reversalTransactionId)
    {
        if (Status != TransactionStatus.Posted && Status != TransactionStatus.Reconciled)
            throw new DomainException($"Solo se pueden reversar transacciones contabilizadas o conciliadas. Estado actual: {Status}");

        Status = TransactionStatus.Reversed;
        UpdatedAt = DateTimeOffset.UtcNow;
        
        Metadata ??= new Dictionary<string, object>();
        Metadata["ReversalTransactionId"] = reversalTransactionId;
        Metadata["ReversedAt"] = DateTimeOffset.UtcNow;
    }

    #endregion

    #region Currency Conversion

    /// <summary>
    /// Aplica conversión de moneda a la transacción.
    /// </summary>
    public void ApplyCurrencyConversion(
        Money convertedAmount,
        decimal exchangeRate,
        Guid? exchangeRateId = null)
    {
        if (!Status.IsModifiable())
            throw new DomainException("No se puede modificar una transacción que ya fue procesada.");

        if (exchangeRate <= 0)
            throw new DomainException("La tasa de cambio debe ser positiva.");

        // Guardar el monto original
        OriginalAmount = Amount;
        
        // Aplicar conversión
        Amount = convertedAmount;
        ExchangeRateUsed = exchangeRate;
        ExchangeRateId = exchangeRateId;
        
        // Recalcular hash con nuevo monto
        Hash = CalculateHash();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    #endregion

    #region Categorization

    /// <summary>
    /// Categoriza la transacción.
    /// </summary>
    public void Categorize(string category, string? subCategory = null)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new DomainException("La categoría no puede estar vacía.");

        Category = category.Trim();
        SubCategory = subCategory?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Agrega etiquetas a la transacción.
    /// </summary>
    public void AddTags(params string[] tags)
    {
        foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            var normalizedTag = tag.Trim().ToLowerInvariant();
            if (!Tags.Contains(normalizedTag))
            {
                Tags.Add(normalizedTag);
            }
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Establece información de la contraparte.
    /// </summary>
    public void SetCounterparty(CounterpartyInfo counterparty)
    {
        Counterparty = counterparty ?? throw new DomainException("La información de contraparte no puede ser nula.");
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    #endregion

    #region Private Methods

    private static void ValidateAmountSign(TransactionType type, Money amount)
    {
        var shouldBeNegative = type.IsDebitType();
        var shouldBePositive = type.IsCreditType();

        if (shouldBeNegative && amount.Amount > 0)
        {
            throw new DomainException(
                $"Las transacciones de tipo {type} deben tener monto negativo. Monto recibido: {amount}");
        }

        if (shouldBePositive && amount.Amount < 0)
        {
            throw new DomainException(
                $"Las transacciones de tipo {type} deben tener monto positivo. Monto recibido: {amount}");
        }
    }

    private void EnsureCanTransition(TransactionStatus targetStatus)
    {
        var validTransitions = new Dictionary<TransactionStatus, TransactionStatus[]>
        {
            [TransactionStatus.Pending] = new[] { TransactionStatus.Processing, TransactionStatus.Rejected },
            [TransactionStatus.Processing] = new[] { TransactionStatus.Validated, TransactionStatus.Rejected },
            [TransactionStatus.Validated] = new[] { TransactionStatus.Posted, TransactionStatus.Rejected },
            [TransactionStatus.Posted] = new[] { TransactionStatus.Reconciled, TransactionStatus.Reversed },
            [TransactionStatus.Reconciled] = new[] { TransactionStatus.Reversed },
        };

        if (!validTransitions.TryGetValue(Status, out var allowedTargets) || 
            !allowedTargets.Contains(targetStatus))
        {
            throw new DomainException(
                $"Transición de estado inválida: {Status} -> {targetStatus}");
        }
    }

    private string CalculateHash()
    {
        var data = $"{ExternalId}|{Amount.Amount}|{ValueDate:yyyy-MM-dd}|{AccountId}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(data);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    #endregion
}

/// <summary>
/// Información de la contraparte de una transacción.
/// </summary>
public record CounterpartyInfo
{
    public string? Name { get; init; }
    public string? AccountNumber { get; init; }
    public string? BankName { get; init; }
    public string? Reference { get; init; }
    public string? TaxId { get; init; }
}
