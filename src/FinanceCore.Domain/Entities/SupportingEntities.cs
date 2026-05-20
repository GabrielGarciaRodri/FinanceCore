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
    private readonly List<ReconciliationDiscrepancy> _discrepancies = new();

    public DateOnly ReconciliationDate { get; private set; }
    public Guid AccountId { get; private set; }
    public ReconciliationStatus Status { get; private set; }
    public int TotalInternalRecords { get; private set; }
    public int TotalExternalRecords { get; private set; }
    public int MatchedCount { get; private set; }
    public int UnmatchedInternal { get; private set; }
    public int UnmatchedExternal { get; private set; }
    public decimal TotalInternalAmount { get; private set; }
    public decimal TotalExternalAmount { get; private set; }
    public decimal DiscrepancyAmount { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public long? DurationMs { get; private set; }
    public string ProcessedBy { get; private set; } = null!;
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? Notes { get; private set; }
    public string? ResolutionNotes { get; private set; }

    public virtual Account? Account { get; private set; }
    public virtual IReadOnlyCollection<ReconciliationDiscrepancy> Discrepancies => _discrepancies.AsReadOnly();

    // EF Core
    private Reconciliation() { }

    public static Reconciliation Start(Guid accountId, DateOnly date, string processedBy, string? notes = null)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId requerido", nameof(accountId));
        if (string.IsNullOrWhiteSpace(processedBy))
            throw new ArgumentException("ProcessedBy requerido", nameof(processedBy));

        return new Reconciliation
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            ReconciliationDate = date,
            Status = ReconciliationStatus.InProgress,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcessedBy = processedBy,
            Notes = notes
        };
    }

    public ReconciliationDiscrepancy AddDiscrepancy(
        DiscrepancyType type,
        Guid? internalTransactionId,
        string? externalReference,
        decimal? internalAmount,
        decimal? externalAmount,
        DateOnly? internalDate,
        DateOnly? externalDate,
        string? notes = null)
    {
        var discrepancy = ReconciliationDiscrepancy.Create(
            Id,
            type,
            internalTransactionId,
            externalReference,
            internalAmount,
            externalAmount,
            internalDate,
            externalDate,
            notes);

        _discrepancies.Add(discrepancy);
        return discrepancy;
    }

    public void Complete(
        int totalInternal,
        int totalExternal,
        int matched,
        int unmatchedInternal,
        int unmatchedExternal,
        decimal totalInternalAmount,
        decimal totalExternalAmount,
        decimal discrepancyAmount)
    {
        TotalInternalRecords = totalInternal;
        TotalExternalRecords = totalExternal;
        MatchedCount = matched;
        UnmatchedInternal = unmatchedInternal;
        UnmatchedExternal = unmatchedExternal;
        TotalInternalAmount = totalInternalAmount;
        TotalExternalAmount = totalExternalAmount;
        DiscrepancyAmount = discrepancyAmount;
        CompletedAt = DateTimeOffset.UtcNow;
        DurationMs = StartedAt.HasValue
            ? (long)(CompletedAt.Value - StartedAt.Value).TotalMilliseconds
            : null;
        Status = (_discrepancies.Count == 0 && unmatchedInternal == 0 && unmatchedExternal == 0)
            ? ReconciliationStatus.Completed
            : ReconciliationStatus.CompletedWithDiscrepancies;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string reason)
    {
        Status = ReconciliationStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
        DurationMs = StartedAt.HasValue
            ? (long)(CompletedAt.Value - StartedAt.Value).TotalMilliseconds
            : null;
        Notes = string.IsNullOrWhiteSpace(Notes) ? reason : $"{Notes} | {reason}";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Approve(string approvedBy, string? resolutionNotes = null)
    {
        if (!Status.IsTerminal())
            throw new InvalidOperationException("Solo se puede aprobar una conciliación en estado terminal.");
        ApprovedBy = approvedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        ResolutionNotes = resolutionNotes;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Detalle de una discrepancia detectada durante la conciliación.
/// </summary>
public class ReconciliationDiscrepancy : BaseEntity
{
    public Guid ReconciliationId { get; private set; }
    public Guid? InternalTransactionId { get; private set; }
    public string? ExternalReference { get; private set; }
    public DiscrepancyType DiscrepancyType { get; private set; }
    public decimal? InternalAmount { get; private set; }
    public decimal? ExternalAmount { get; private set; }
    public decimal? DifferenceAmount { get; private set; }
    public DateOnly? InternalDate { get; private set; }
    public DateOnly? ExternalDate { get; private set; }
    public bool IsResolved { get; private set; }
    public ResolutionType? ResolutionType { get; private set; }
    public string? ResolutionNotes { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }

    private ReconciliationDiscrepancy() { }

    internal static ReconciliationDiscrepancy Create(
        Guid reconciliationId,
        DiscrepancyType type,
        Guid? internalTransactionId,
        string? externalReference,
        decimal? internalAmount,
        decimal? externalAmount,
        DateOnly? internalDate,
        DateOnly? externalDate,
        string? notes)
    {
        return new ReconciliationDiscrepancy
        {
            Id = Guid.NewGuid(),
            ReconciliationId = reconciliationId,
            DiscrepancyType = type,
            InternalTransactionId = internalTransactionId,
            ExternalReference = externalReference,
            InternalAmount = internalAmount,
            ExternalAmount = externalAmount,
            DifferenceAmount = (internalAmount.HasValue && externalAmount.HasValue)
                ? externalAmount.Value - internalAmount.Value
                : null,
            InternalDate = internalDate,
            ExternalDate = externalDate,
            IsResolved = false,
            ResolutionNotes = notes,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Resolve(ResolutionType resolution, string resolvedBy, string? notes = null)
    {
        if (IsResolved)
            return;

        IsResolved = resolution != Enums.ResolutionType.Pending
                  && resolution != Enums.ResolutionType.UnderInvestigation;
        ResolutionType = resolution;
        ResolvedBy = resolvedBy;
        ResolutionNotes = notes ?? ResolutionNotes;
        ResolvedAt = IsResolved ? DateTimeOffset.UtcNow : null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Línea de extracto bancario externa usada para conciliación.
/// Estructura ligera utilizada por el motor — no se persiste como entidad propia.
/// </summary>
public record ExternalStatementLine(
    string ExternalReference,
    decimal Amount,
    string CurrencyCode,
    DateOnly ValueDate,
    string? Description = null);

/// <summary>
/// Marca de la fuente externa usada para conciliar.
/// </summary>
public enum ReconciliationSource
{
    /// <summary>Sólo balance reportado (DailyBalance.ClosingBalance).</summary>
    BalanceOnly = 1,

    /// <summary>Lista de transacciones externas (extracto bancario).</summary>
    StatementTransactions = 2
}
