namespace FinanceCore.Domain.Enums;

/// <summary>
/// Tipo de regla de alerta de negocio (SCRUM-45).
/// </summary>
public enum AlertRuleType
{
    /// <summary>No llegó el payout esperado de una fuente en N días</summary>
    MissingPayout = 1,

    /// <summary>Conciliación con discrepancia mayor al umbral ($ o %)</summary>
    DiscrepancyThreshold = 2,

    /// <summary>Saldo de cuenta por debajo del umbral</summary>
    LowBalance = 3
}

/// <summary>
/// Canales de entrega de una alerta de negocio. Combinables.
/// </summary>
[Flags]
public enum AlertChannels
{
    None = 0,
    Email = 1,
    Webhook = 2
}

/// <summary>
/// Severidad de un disparo de alerta de negocio, agnóstica del mecanismo de
/// entrega (Infrastructure la mapea a su propia escala).
/// </summary>
public enum AlertTriggerSeverity
{
    Warning = 1,
    Error = 2,
    Critical = 3
}
