using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;

namespace FinanceCore.Domain.Services;

/// <summary>
/// Disparo de una regla de alerta: qué contar y con qué severidad.
/// La entrega (email/webhook) la resuelve Infrastructure con los canales de la regla.
/// </summary>
public sealed record AlertTrigger(
    AlertTriggerSeverity Severity,
    string Title,
    string Message);

/// <summary>
/// Evaluación pura de reglas de alerta de negocio (SCRUM-45). Sin I/O:
/// recibe la regla y los hechos ya cargados, devuelve el disparo o null.
/// El cooldown/enabled se decide antes con <see cref="AlertRule.CanTrigger"/>.
/// </summary>
public static class AlertRuleEvaluator
{
    /// <summary>
    /// Payout esperado que no llegó: dispara cuando pasaron más de
    /// LookbackDays desde el último payout conocido de la fuente. Para una
    /// regla que nunca vio un payout, el punto de partida es la creación de
    /// la regla (no dispara recién configurada).
    /// </summary>
    public static AlertTrigger? EvaluateMissingPayout(
        AlertRule rule,
        DateOnly? lastPayoutDate,
        DateOnly today,
        string sourceDisplayName)
    {
        if (rule.Type != AlertRuleType.MissingPayout || rule.LookbackDays is not int lookback)
            return null;

        var baseline = lastPayoutDate ?? DateOnly.FromDateTime(rule.CreatedAt.UtcDateTime);
        var daysSince = today.DayNumber - baseline.DayNumber;

        if (daysSince <= lookback)
            return null;

        var severity = daysSince > lookback * 2
            ? AlertTriggerSeverity.Critical
            : AlertTriggerSeverity.Error;

        var lastSeen = lastPayoutDate?.ToString("yyyy-MM-dd") ?? "nunca";

        return new AlertTrigger(
            severity,
            $"El payout de {sourceDisplayName} no llegó",
            $"Sin payout de {sourceDisplayName} hace {daysSince} días " +
            $"(esperado cada {lookback}; último: {lastSeen}).");
    }

    /// <summary>
    /// Discrepancia de conciliación sobre el umbral de la regla ($ y/o %).
    /// Con ambos umbrales configurados dispara el primero que se supere.
    /// </summary>
    public static AlertTrigger? EvaluateDiscrepancy(
        AlertRule rule,
        decimal discrepancyAmount,
        decimal totalExternalAmount,
        DateOnly reconciliationDate,
        string accountName)
    {
        if (rule.Type != AlertRuleType.DiscrepancyThreshold)
            return null;

        var abs = Math.Abs(discrepancyAmount);

        var overAmount = rule.ThresholdAmount is decimal amount && abs >= amount;
        var overPercent = rule.ThresholdPercent is decimal percent
            && totalExternalAmount > 0
            && abs / totalExternalAmount >= percent;

        if (!overAmount && !overPercent)
            return null;

        // Misma heurística del handler de sistema: 10x el umbral = crítico.
        var severity = rule.ThresholdAmount is decimal a && abs >= a * 10
            ? AlertTriggerSeverity.Critical
            : AlertTriggerSeverity.Error;

        var reason = overAmount
            ? $"supera el umbral de {rule.ThresholdAmount:N2}"
            : $"supera el {rule.ThresholdPercent:P2} del total conciliado";

        return new AlertTrigger(
            severity,
            $"Discrepancia sobre umbral en {accountName}",
            $"Conciliación del {reconciliationDate:yyyy-MM-dd}: discrepancia " +
            $"{discrepancyAmount:N2} {reason} (total externo {totalExternalAmount:N2}).");
    }

    /// <summary>
    /// Saldo de cuenta por debajo del umbral. Saldo negativo = crítico.
    /// </summary>
    public static AlertTrigger? EvaluateLowBalance(
        AlertRule rule,
        decimal currentBalance,
        string currencyCode,
        string accountName)
    {
        if (rule.Type != AlertRuleType.LowBalance || rule.ThresholdAmount is not decimal threshold)
            return null;

        if (currentBalance >= threshold)
            return null;

        var severity = currentBalance < 0
            ? AlertTriggerSeverity.Critical
            : AlertTriggerSeverity.Warning;

        return new AlertTrigger(
            severity,
            $"Saldo bajo en {accountName}",
            $"Saldo actual {currentBalance:N2} {currencyCode}, por debajo del " +
            $"umbral de {threshold:N2} {currencyCode}.");
    }
}
