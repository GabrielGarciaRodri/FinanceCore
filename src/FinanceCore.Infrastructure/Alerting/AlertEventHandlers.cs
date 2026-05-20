using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinanceCore.Domain.Events;
using FinanceCore.Infrastructure.Reconciliations;

namespace FinanceCore.Infrastructure.Alerting;

/// <summary>
/// Convierte ReconciliationCompletedEvent en alerta cuando el descuadre supera
/// el umbral configurado (ReconciliationOptions.SignificantDiscrepancyThreshold).
/// </summary>
public class ReconciliationDiscrepancyAlertHandler : INotificationHandler<ReconciliationCompletedEvent>
{
    private readonly IAlertDispatcher _dispatcher;
    private readonly ReconciliationOptions _reconciliationOptions;
    private readonly ILogger<ReconciliationDiscrepancyAlertHandler> _logger;

    public ReconciliationDiscrepancyAlertHandler(
        IAlertDispatcher dispatcher,
        IOptions<ReconciliationOptions> reconciliationOptions,
        ILogger<ReconciliationDiscrepancyAlertHandler> logger)
    {
        _dispatcher = dispatcher;
        _reconciliationOptions = reconciliationOptions.Value;
        _logger = logger;
    }

    public async Task Handle(ReconciliationCompletedEvent notification, CancellationToken cancellationToken)
    {
        var absDiscrepancy = Math.Abs(notification.DiscrepancyAmount);
        var threshold = _reconciliationOptions.SignificantDiscrepancyThreshold;

        if (!notification.HasDiscrepancies && absDiscrepancy < threshold)
            return;

        var severity = absDiscrepancy >= threshold * 10
            ? AlertSeverity.Critical
            : absDiscrepancy >= threshold
                ? AlertSeverity.Error
                : AlertSeverity.Warning;

        var alert = new Alert(
            Type: "reconciliation.discrepancy",
            Severity: severity,
            Title: $"Reconciliación con descuadre — cuenta {notification.AccountId:N}",
            Message: $"Fecha {notification.ReconciliationDate:yyyy-MM-dd}: descuadre " +
                     $"{notification.DiscrepancyAmount:N2}, no conciliadas {notification.UnmatchedCount}.",
            OccurredAt: notification.OccurredAt,
            Properties: new Dictionary<string, object?>
            {
                ["reconciliationId"] = notification.ReconciliationId,
                ["accountId"] = notification.AccountId,
                ["date"] = notification.ReconciliationDate.ToString("yyyy-MM-dd"),
                ["matched"] = notification.MatchedCount,
                ["unmatched"] = notification.UnmatchedCount,
                ["discrepancyAmount"] = notification.DiscrepancyAmount,
                ["threshold"] = threshold
            });

        _logger.LogInformation(
            "Dispatching reconciliation discrepancy alert ({Severity}) for {ReconciliationId}",
            severity, notification.ReconciliationId);

        await _dispatcher.DispatchAsync(alert, cancellationToken);
    }
}

/// <summary>
/// Re-emite FinancialAnomalyDetectedEvent como alerta. Severidad mapeada 1:1.
/// </summary>
public class FinancialAnomalyAlertHandler : INotificationHandler<FinancialAnomalyDetectedEvent>
{
    private readonly IAlertDispatcher _dispatcher;

    public FinancialAnomalyAlertHandler(IAlertDispatcher dispatcher) => _dispatcher = dispatcher;

    public async Task Handle(FinancialAnomalyDetectedEvent notification, CancellationToken cancellationToken)
    {
        var severity = notification.Severity?.ToLowerInvariant() switch
        {
            "critical" => AlertSeverity.Critical,
            "high"     => AlertSeverity.Error,
            "medium"   => AlertSeverity.Warning,
            _          => AlertSeverity.Info
        };

        var alert = new Alert(
            Type: $"anomaly.{notification.AnomalyType}",
            Severity: severity,
            Title: $"Anomalía financiera detectada: {notification.AnomalyType}",
            Message: notification.Description,
            OccurredAt: notification.OccurredAt,
            Properties: new Dictionary<string, object?>
            {
                ["accountId"] = notification.AccountId,
                ["transactionId"] = notification.TransactionId,
                ["amount"] = notification.Amount,
                ["anomalyType"] = notification.AnomalyType
            });

        await _dispatcher.DispatchAsync(alert, cancellationToken);
    }
}

/// <summary>
/// Alerta de cambio grande en balance: usa el threshold de reconciliación
/// como heurística (multiplicado x10 para "cambios grandes de cuenta").
/// </summary>
public class AccountBalanceAlertHandler : INotificationHandler<AccountBalanceChangedEvent>
{
    private readonly IAlertDispatcher _dispatcher;
    private readonly ReconciliationOptions _reconciliationOptions;

    public AccountBalanceAlertHandler(
        IAlertDispatcher dispatcher,
        IOptions<ReconciliationOptions> reconciliationOptions)
    {
        _dispatcher = dispatcher;
        _reconciliationOptions = reconciliationOptions.Value;
    }

    public async Task Handle(AccountBalanceChangedEvent notification, CancellationToken cancellationToken)
    {
        var threshold = _reconciliationOptions.SignificantDiscrepancyThreshold * 100;
        if (Math.Abs(notification.Change) < threshold)
            return;

        var alert = new Alert(
            Type: "account.balance_change",
            Severity: AlertSeverity.Warning,
            Title: $"Cambio grande de saldo en cuenta {notification.AccountId:N}",
            Message: $"Saldo cambió de {notification.PreviousBalance:N2} a {notification.NewBalance:N2} " +
                     $"({notification.Change:+#.##;-#.##;0} {notification.CurrencyCode}).",
            OccurredAt: notification.OccurredAt,
            Properties: new Dictionary<string, object?>
            {
                ["accountId"] = notification.AccountId,
                ["previous"] = notification.PreviousBalance,
                ["new"] = notification.NewBalance,
                ["change"] = notification.Change,
                ["currency"] = notification.CurrencyCode
            });

        await _dispatcher.DispatchAsync(alert, cancellationToken);
    }
}
