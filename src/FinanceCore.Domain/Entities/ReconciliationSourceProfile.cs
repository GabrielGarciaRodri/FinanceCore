using System.Text.RegularExpressions;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Exceptions;

namespace FinanceCore.Domain.Entities;

/// <summary>
/// Perfil de conciliación por pasarela/fuente para el matching N:1:
/// cómo reconocer sus payouts en el extracto bancario, cómo identificar sus
/// ventas internas, qué comisión esperar y cuántos días agrupa un payout.
/// Ver docs/design/SCRUM-41-group-matching.md.
/// </summary>
public class ReconciliationSourceProfile : BaseEntity, IAggregateRoot
{
    /// <summary>Cuenta a la que aplica el perfil; null = todas las cuentas.</summary>
    public Guid? AccountId { get; private set; }

    /// <summary>Nombre canónico de la fuente (ej: "PAYU").</summary>
    public string SourceKey { get; private set; } = null!;

    /// <summary>Nombre visible (ej: "PayU Colombia").</summary>
    public string DisplayName { get; private set; } = null!;

    /// <summary>
    /// Regex (case-insensitive) sobre referencia + descripción de la línea de
    /// extracto que identifica una línea como payout de esta fuente.
    /// </summary>
    public string PayoutPattern { get; private set; } = null!;

    /// <summary>Campo de la transacción interna que identifica las ventas de la fuente.</summary>
    public InternalMatchField InternalMatchField { get; private set; }

    /// <summary>Regex (case-insensitive) sobre el campo elegido.</summary>
    public string InternalMatchPattern { get; private set; } = null!;

    /// <summary>Comisión esperada como fracción (0.035 = 3.5%).</summary>
    public decimal ExpectedFeePercent { get; private set; }

    /// <summary>Semibanda de tolerancia alrededor de la comisión esperada (0.005 = ±0.5%).</summary>
    public decimal FeeTolerancePercent { get; private set; }

    /// <summary>Días hacia atrás desde la fecha del payout que puede cubrir un grupo.</summary>
    public int GroupingWindowDays { get; private set; }

    public bool IsActive { get; private set; }

    // EF Core
    private ReconciliationSourceProfile() { }

    public static ReconciliationSourceProfile Create(
        Guid? accountId,
        string sourceKey,
        string displayName,
        string payoutPattern,
        InternalMatchField internalMatchField,
        string internalMatchPattern,
        decimal expectedFeePercent,
        decimal feeTolerancePercent,
        int groupingWindowDays)
    {
        var profile = new ReconciliationSourceProfile
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        profile.Apply(
            sourceKey, displayName, payoutPattern, internalMatchField,
            internalMatchPattern, expectedFeePercent, feeTolerancePercent, groupingWindowDays);

        return profile;
    }

    public void Update(
        string sourceKey,
        string displayName,
        string payoutPattern,
        InternalMatchField internalMatchField,
        string internalMatchPattern,
        decimal expectedFeePercent,
        decimal feeTolerancePercent,
        int groupingWindowDays,
        bool isActive)
    {
        Apply(
            sourceKey, displayName, payoutPattern, internalMatchField,
            internalMatchPattern, expectedFeePercent, feeTolerancePercent, groupingWindowDays);
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void Apply(
        string sourceKey,
        string displayName,
        string payoutPattern,
        InternalMatchField internalMatchField,
        string internalMatchPattern,
        decimal expectedFeePercent,
        decimal feeTolerancePercent,
        int groupingWindowDays)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            throw new DomainException("SourceKey es requerido.");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("DisplayName es requerido.");
        if (expectedFeePercent is < 0m or >= 0.5m)
            throw new DomainException("La comisión esperada debe estar entre 0 y 50%.");
        if (feeTolerancePercent is < 0m or >= 0.2m)
            throw new DomainException("La tolerancia de comisión debe estar entre 0 y 20%.");
        if (groupingWindowDays is < 1 or > 62)
            throw new DomainException("La ventana de agrupación debe estar entre 1 y 62 días.");

        ValidateRegex(payoutPattern, nameof(PayoutPattern));
        ValidateRegex(internalMatchPattern, nameof(InternalMatchPattern));

        SourceKey = sourceKey.Trim().ToUpperInvariant();
        DisplayName = displayName.Trim();
        PayoutPattern = payoutPattern.Trim();
        InternalMatchField = internalMatchField;
        InternalMatchPattern = internalMatchPattern.Trim();
        ExpectedFeePercent = expectedFeePercent;
        FeeTolerancePercent = feeTolerancePercent;
        GroupingWindowDays = groupingWindowDays;
    }

    private static void ValidateRegex(string pattern, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new DomainException($"{fieldName} es requerido.");

        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException ex)
        {
            throw new DomainException($"{fieldName} no es una regex válida: {ex.Message}");
        }
    }
}
