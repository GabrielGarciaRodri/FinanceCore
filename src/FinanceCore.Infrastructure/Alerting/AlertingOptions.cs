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

    /// <summary>
    /// Habilita el envío de emails vía Resend (reglas de negocio, SCRUM-45).
    /// </summary>
    public bool EmailEnabled { get; set; } = false;

    /// <summary>
    /// API key de Resend. En producción va por env var
    /// (FinanceCore__Alerting__ResendApiKey), nunca en appsettings.
    /// </summary>
    public string? ResendApiKey { get; set; }

    /// <summary>
    /// Remitente. El default de Resend funciona sin verificar dominio propio.
    /// </summary>
    public string EmailFrom { get; set; } = "FinanceCore <onboarding@resend.dev>";

    /// <summary>
    /// Destinatario por defecto cuando la regla no define el suyo.
    /// </summary>
    public string? EmailTo { get; set; }

    /// <summary>
    /// Timeout para la llamada a la API de Resend.
    /// </summary>
    public int EmailTimeoutSeconds { get; set; } = 10;
}
