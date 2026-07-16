using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Services;

namespace FinanceCore.Infrastructure.Alerting;

/// <summary>
/// Convierte el disparo de una regla de negocio (dominio) en un Alert de
/// infraestructura, ruteado a los canales que la regla eligió. El sink de
/// logging siempre recibe copia (observabilidad).
/// </summary>
public static class BusinessAlertMapper
{
    public static Alert ToAlert(
        AlertRule rule,
        AlertTrigger trigger,
        IReadOnlyDictionary<string, object?> properties)
    {
        var channels = new List<string> { "logging" };
        if (rule.Channels.HasFlag(AlertChannels.Email))
            channels.Add("email");
        if (rule.Channels.HasFlag(AlertChannels.Webhook))
            channels.Add("webhook");

        var severity = trigger.Severity switch
        {
            AlertTriggerSeverity.Critical => AlertSeverity.Critical,
            AlertTriggerSeverity.Error    => AlertSeverity.Error,
            _                             => AlertSeverity.Warning
        };

        var props = new Dictionary<string, object?>(properties)
        {
            ["ruleId"] = rule.Id,
            ["ruleName"] = rule.Name
        };

        return new Alert(
            Type: $"business.{rule.Type.ToString().ToLowerInvariant()}",
            Severity: severity,
            Title: trigger.Title,
            Message: trigger.Message,
            OccurredAt: DateTimeOffset.UtcNow,
            Properties: props,
            Channels: channels,
            EmailToOverride: rule.EmailTo);
    }
}
