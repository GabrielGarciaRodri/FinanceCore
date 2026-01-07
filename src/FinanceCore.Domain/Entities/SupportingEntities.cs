using FinanceCore.Domain.Enums;
using FinanceCore.Domain.ValueObjects;

namespace FinanceCore.Domain.Entities;

/// <summary>
/// Institución financiera (banco, pasarela de pago, etc.)
/// </summary>
public class Institution : BaseEntity, IAggregateRoot
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
    public string? SwiftCode { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Balance diario de una cuenta.
/// </summary>
public class DailyBalance : BaseEntity
{
    public Guid AccountId { get; set; }
    public DateOnly BalanceDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public decimal TotalDebits { get; set; }
    public decimal TotalCredits { get; set; }
    public int TransactionCount { get; set; }
    public bool IsReconciled { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }
    public string? ReconciledBy { get; set; }

    public virtual Account? Account { get; set; }

    public void Update(decimal opening, decimal closing, decimal debits, decimal credits, int count)
    {
        OpeningBalance = opening;
        ClosingBalance = closing;
        TotalDebits = debits;
        TotalCredits = credits;
        TransactionCount = count;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Tipo de cambio entre monedas.
/// </summary>
public class ExchangeRate : BaseEntity
{
    public string FromCurrency { get; set; } = null!;
    public string ToCurrency { get; set; } = null!;
    public decimal Rate { get; set; }
    public decimal InverseRate { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public TimeOnly? EffectiveTime { get; set; }
    public string Source { get; set; } = null!;
    public bool IsOfficial { get; set; }
}

/// <summary>
/// Entrada contable (partida doble).
/// </summary>
public class FinancialEntry : BaseEntity
{
    public Guid TransactionId { get; set; }
    public string LedgerAccount { get; set; } = null!;
    public string? LedgerAccountName { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public DateOnly EntryDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public Guid? ContraEntryId { get; set; }

    public virtual Transaction? Transaction { get; set; }
}

/// <summary>
/// Origen de una transacción (trazabilidad ETL).
/// </summary>
public class TransactionSource : BaseEntity
{
    public Guid TransactionId { get; set; }
    public SourceType SourceType { get; set; }
    public string? SourceFile { get; set; }
    public int? SourceLine { get; set; }
    public string? SourceApi { get; set; }
    public Dictionary<string, object> RawData { get; set; } = new();
    public string Checksum { get; set; } = null!;
    public Dictionary<string, object>? TransformationLog { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
    public Guid? IngestionBatchId { get; set; }

    public virtual Transaction? Transaction { get; set; }
}

/// <summary>
/// Registro de conciliación.
/// </summary>
public class Reconciliation : BaseEntity, IAggregateRoot
{
    public DateOnly ReconciliationDate { get; set; }
    public Guid AccountId { get; set; }
    public ReconciliationStatus Status { get; set; }
    public int TotalInternalRecords { get; set; }
    public int TotalExternalRecords { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedInternal { get; set; }
    public int UnmatchedExternal { get; set; }
    public decimal TotalInternalAmount { get; set; }
    public decimal TotalExternalAmount { get; set; }
    public decimal DiscrepancyAmount { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public string ProcessedBy { get; set; } = null!;
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? Notes { get; set; }
    public string? ResolutionNotes { get; set; }

    public virtual Account? Account { get; set; }
}
