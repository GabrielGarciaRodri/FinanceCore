using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceCore.Infrastructure.Alerting;

public enum AlertSeverity { Info = 0, Warning = 1, Error = 2, Critical = 3 }

public sealed record Alert(
    string Type,
    AlertSeverity Severity,
    string Title,
    string Message,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, object?> Properties);

public interface IAlertSink
{
    string Name { get; }
    Task SendAsync(Alert alert, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fan-out a todos los sinks registrados. Errores en un sink no abortan el resto.
/// </summary>
public interface IAlertDispatcher
{
    Task DispatchAsync(Alert alert, CancellationToken cancellationToken = default);
}

public class AlertDispatcher : IAlertDispatcher
{
    private readonly IEnumerable<IAlertSink> _sinks;
    private readonly AlertingOptions _options;
    private readonly ILogger<AlertDispatcher> _logger;
    private readonly AlertSeverity _minSeverity;

    public AlertDispatcher(
        IEnumerable<IAlertSink> sinks,
        IOptions<AlertingOptions> options,
        ILogger<AlertDispatcher> logger)
    {
        _sinks = sinks;
        _options = options.Value;
        _logger = logger;
        _minSeverity = Enum.TryParse<AlertSeverity>(_options.MinimumSeverity, ignoreCase: true, out var sev)
            ? sev
            : AlertSeverity.Warning;
    }

    public async Task DispatchAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;
        if (alert.Severity < _minSeverity) return;

        foreach (var sink in _sinks)
        {
            try
            {
                await sink.SendAsync(alert, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Alert sink {Sink} failed for {AlertType}",
                    sink.Name, alert.Type);
            }
        }
    }
}

/// <summary>
/// Sink always-on: emite un log estructurado con la alerta.
/// Cualquier APM/log pipeline ya conectado (Serilog/OTel) lo captura.
/// </summary>
public class LoggingAlertSink : IAlertSink
{
    private readonly ILogger<LoggingAlertSink> _logger;

    public LoggingAlertSink(ILogger<LoggingAlertSink> logger) => _logger = logger;

    public string Name => "logging";

    public Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        var logLevel = alert.Severity switch
        {
            AlertSeverity.Critical => LogLevel.Critical,
            AlertSeverity.Error    => LogLevel.Error,
            AlertSeverity.Warning  => LogLevel.Warning,
            _                      => LogLevel.Information
        };

        using var scope = _logger.BeginScope(alert.Properties);
        _logger.Log(logLevel,
            "[Alert:{Type}] {Title} — {Message}",
            alert.Type, alert.Title, alert.Message);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Sink HTTP genérico: POST JSON con la estructura completa de la alerta.
/// Compatible con Slack/Teams/Discord/ServiceNow o cualquier webhook custom.
/// </summary>
public class WebhookAlertSink : IAlertSink
{
    private readonly HttpClient _httpClient;
    private readonly AlertingOptions _options;
    private readonly ILogger<WebhookAlertSink> _logger;

    public WebhookAlertSink(
        HttpClient httpClient,
        IOptions<AlertingOptions> options,
        ILogger<WebhookAlertSink> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.WebhookTimeoutSeconds));
    }

    public string Name => "webhook";

    public async Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        if (!_options.WebhookEnabled || string.IsNullOrWhiteSpace(_options.WebhookUrl))
            return;

        var payload = new
        {
            type = alert.Type,
            severity = alert.Severity.ToString(),
            title = alert.Title,
            message = alert.Message,
            occurredAt = alert.OccurredAt,
            properties = alert.Properties
        };

        var response = await _httpClient.PostAsJsonAsync(_options.WebhookUrl, payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Webhook responded {StatusCode} for alert {AlertType}",
                (int)response.StatusCode, alert.Type);
        }
    }
}
