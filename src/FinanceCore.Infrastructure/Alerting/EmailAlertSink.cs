using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceCore.Infrastructure.Alerting;

/// <summary>
/// Sink de email vía la API HTTP de Resend (SCRUM-45). Free tier: 100
/// emails/día — suficiente para alertas de negocio con cooldown. Sin API key
/// o con EmailEnabled=false el sink es un no-op (el LoggingAlertSink sigue
/// registrando la alerta).
/// </summary>
public class EmailAlertSink : IAlertSink
{
    private const string ResendEndpoint = "https://api.resend.com/emails";

    private readonly HttpClient _httpClient;
    private readonly AlertingOptions _options;
    private readonly ILogger<EmailAlertSink> _logger;

    public EmailAlertSink(
        HttpClient httpClient,
        IOptions<AlertingOptions> options,
        ILogger<EmailAlertSink> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.EmailTimeoutSeconds));
    }

    public string Name => "email";

    public async Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        if (!_options.EmailEnabled || string.IsNullOrWhiteSpace(_options.ResendApiKey))
            return;

        var to = alert.EmailToOverride ?? _options.EmailTo;
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning(
                "Email alert {AlertType} sin destinatario: ni la regla ni la config definen uno",
                alert.Type);
            return;
        }

        var payload = new
        {
            from = _options.EmailFrom,
            to = new[] { to },
            subject = $"[FinanceCore · {alert.Severity}] {alert.Title}",
            html = BuildHtml(alert)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ResendEndpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Resend respondió {StatusCode} para la alerta {AlertType}: {Body}",
                (int)response.StatusCode, alert.Type, body);
        }
        else
        {
            _logger.LogInformation(
                "Email de alerta {AlertType} enviado a {To}", alert.Type, to);
        }
    }

    private static string BuildHtml(Alert alert)
    {
        var color = alert.Severity switch
        {
            AlertSeverity.Critical => "#dc2626",
            AlertSeverity.Error    => "#ea580c",
            AlertSeverity.Warning  => "#ca8a04",
            _                      => "#2563eb"
        };

        var rows = string.Join("", alert.Properties
            .Where(p => p.Value != null)
            .Select(p =>
                $"<tr><td style=\"padding:4px 12px 4px 0;color:#6b7280;\">{WebUtility.HtmlEncode(p.Key)}</td>" +
                $"<td style=\"padding:4px 0;\">{WebUtility.HtmlEncode(p.Value!.ToString())}</td></tr>"));

        return $"""
            <div style="font-family:ui-sans-serif,system-ui,sans-serif;max-width:560px;margin:0 auto;padding:24px;">
              <p style="margin:0 0 4px;font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:{color};font-weight:600;">
                {WebUtility.HtmlEncode(alert.Severity.ToString())}
              </p>
              <h2 style="margin:0 0 12px;font-size:18px;color:#111827;">{WebUtility.HtmlEncode(alert.Title)}</h2>
              <p style="margin:0 0 16px;font-size:14px;line-height:1.6;color:#374151;">{WebUtility.HtmlEncode(alert.Message)}</p>
              <table style="font-size:13px;border-collapse:collapse;color:#111827;">{rows}</table>
              <p style="margin:24px 0 0;font-size:12px;color:#9ca3af;">
                {alert.OccurredAt:yyyy-MM-dd HH:mm} UTC · FinanceCore
              </p>
            </div>
            """;
    }
}
