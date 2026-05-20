namespace FinanceCore.Infrastructure.Alerting;

public class AlertingOptions
{
    public const string SectionName = "FinanceCore:Alerting";

    /// <summary>
    /// Si false, todos los sinks ignoran las alertas (útil para entornos efímeros).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Habilita el envío al webhook configurado.
    /// </summary>
    public bool WebhookEnabled { get; set; } = false;

    /// <summary>
    /// URL del webhook (Slack, Teams, Discord, ServiceNow, etc.).
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Timeout para la llamada al webhook.
    /// </summary>
    public int WebhookTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Severidad mínima para que un alerta se procese. Valores: Info, Warning, Error, Critical.
    /// </summary>
    public string MinimumSeverity { get; set; } = "Warning";
}
