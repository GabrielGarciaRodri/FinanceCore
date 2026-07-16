using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Exceptions;

namespace FinanceCore.Domain.Entities;

/// <summary>
/// Regla de alerta de negocio configurable por el usuario (SCRUM-45):
/// qué condición vigilar (payout que no llegó, discrepancia sobre umbral,
/// saldo bajo), sobre qué alcance (cuenta / perfil de fuente) y por qué
/// canal avisar. El anti-spam es por cooldown: una regla que disparó no
/// vuelve a disparar hasta que pasen <see cref="CooldownHours"/> horas.
/// </summary>
public class AlertRule : BaseEntity, IAggregateRoot
{
    /// <summary>Nombre visible de la regla (ej: "Payout PayU no llegó").</summary>
    public string Name { get; private set; } = null!;

    public AlertRuleType Type { get; private set; }

    /// <summary>
    /// Cuenta vigilada. Requerida para LowBalance; en DiscrepancyThreshold
    /// null = todas las cuentas.
    /// </summary>
    public Guid? AccountId { get; private set; }

    /// <summary>
    /// Perfil de fuente vigilado (matching N:1). Requerido para MissingPayout.
    /// </summary>
    public Guid? SourceProfileId { get; private set; }

    /// <summary>
    /// Umbral absoluto en la moneda de la cuenta. Requerido para LowBalance;
    /// en DiscrepancyThreshold alternativo a <see cref="ThresholdPercent"/>.
    /// </summary>
    public decimal? ThresholdAmount { get; private set; }

    /// <summary>
    /// Umbral como fracción del total externo conciliado (0.02 = 2%).
    /// Sólo aplica a DiscrepancyThreshold.
    /// </summary>
    public decimal? ThresholdPercent { get; private set; }

    /// <summary>
    /// Días sin payout de la fuente antes de disparar. Sólo MissingPayout.
    /// </summary>
    public int? LookbackDays { get; private set; }

    public AlertChannels Channels { get; private set; }

    /// <summary>Destinatario del email; null = el destinatario global configurado.</summary>
    public string? EmailTo { get; private set; }

    /// <summary>Horas mínimas entre disparos consecutivos de la misma regla.</summary>
    public int CooldownHours { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset? LastTriggeredAt { get; private set; }

    // EF Core
    private AlertRule() { }

    public static AlertRule Create(
        string name,
        AlertRuleType type,
        Guid? accountId,
        Guid? sourceProfileId,
        decimal? thresholdAmount,
        decimal? thresholdPercent,
        int? lookbackDays,
        AlertChannels channels,
        string? emailTo,
        int cooldownHours = 24)
    {
        var rule = new AlertRule
        {
            Id = Guid.NewGuid(),
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        rule.Apply(
            name, type, accountId, sourceProfileId, thresholdAmount,
            thresholdPercent, lookbackDays, channels, emailTo, cooldownHours);

        return rule;
    }

    public void Update(
        string name,
        Guid? accountId,
        Guid? sourceProfileId,
        decimal? thresholdAmount,
        decimal? thresholdPercent,
        int? lookbackDays,
        AlertChannels channels,
        string? emailTo,
        int cooldownHours,
        bool isEnabled)
    {
        // El tipo no cambia: una regla de otro tipo es otra regla.
        Apply(
            name, Type, accountId, sourceProfileId, thresholdAmount,
            thresholdPercent, lookbackDays, channels, emailTo, cooldownHours);
        IsEnabled = isEnabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// La regla puede evaluar: está habilitada y fuera de la ventana de cooldown.
    /// </summary>
    public bool CanTrigger(DateTimeOffset now)
        => IsEnabled &&
           (LastTriggeredAt == null || now - LastTriggeredAt.Value >= TimeSpan.FromHours(CooldownHours));

    public void MarkTriggered(DateTimeOffset now)
    {
        LastTriggeredAt = now;
        UpdatedAt = now;
    }

    private void Apply(
        string name,
        AlertRuleType type,
        Guid? accountId,
        Guid? sourceProfileId,
        decimal? thresholdAmount,
        decimal? thresholdPercent,
        int? lookbackDays,
        AlertChannels channels,
        string? emailTo,
        int cooldownHours)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name es requerido.");
        if (!Enum.IsDefined(type))
            throw new DomainException("Tipo de regla inválido.");
        if (channels == AlertChannels.None)
            throw new DomainException("La regla necesita al menos un canal de entrega.");
        if (cooldownHours is < 1 or > 168)
            throw new DomainException("El cooldown debe estar entre 1 y 168 horas.");
        if (thresholdAmount is < 0m)
            throw new DomainException("El umbral no puede ser negativo.");
        if (thresholdPercent is <= 0m or >= 1m)
            throw new DomainException("El umbral porcentual debe estar entre 0 y 1 (exclusivo).");

        switch (type)
        {
            case AlertRuleType.MissingPayout:
                if (sourceProfileId == null)
                    throw new DomainException("MissingPayout requiere un perfil de fuente.");
                if (lookbackDays is null or < 1 or > 62)
                    throw new DomainException("MissingPayout requiere LookbackDays entre 1 y 62.");
                break;

            case AlertRuleType.DiscrepancyThreshold:
                if (thresholdAmount == null && thresholdPercent == null)
                    throw new DomainException("DiscrepancyThreshold requiere umbral en monto o porcentaje.");
                break;

            case AlertRuleType.LowBalance:
                if (accountId == null)
                    throw new DomainException("LowBalance requiere una cuenta.");
                if (thresholdAmount == null)
                    throw new DomainException("LowBalance requiere un umbral de saldo.");
                break;
        }

        Name = name.Trim();
        Type = type;
        AccountId = accountId;
        SourceProfileId = sourceProfileId;
        ThresholdAmount = thresholdAmount;
        ThresholdPercent = thresholdPercent;
        LookbackDays = lookbackDays;
        Channels = channels;
        EmailTo = string.IsNullOrWhiteSpace(emailTo) ? null : emailTo.Trim();
        CooldownHours = cooldownHours;
    }
}
