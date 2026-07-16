using MediatR;
using Microsoft.Extensions.Logging;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Events;
using FinanceCore.Domain.Repositories;
using FinanceCore.Domain.Services;

namespace FinanceCore.Infrastructure.Alerting;

/// <summary>
/// Evalúa las reglas de negocio DiscrepancyThreshold (SCRUM-45) cuando una
/// conciliación termina. A diferencia del handler de sistema (umbral global
/// de config), acá los umbrales y canales los define el usuario por regla.
/// </summary>
public class BusinessRuleAlertHandler : INotificationHandler<ReconciliationCompletedEvent>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAlertDispatcher _dispatcher;
    private readonly ILogger<BusinessRuleAlertHandler> _logger;

    public BusinessRuleAlertHandler(
        IUnitOfWork unitOfWork,
        IAlertDispatcher dispatcher,
        ILogger<BusinessRuleAlertHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Handle(ReconciliationCompletedEvent notification, CancellationToken cancellationToken)
    {
        var rules = await _unitOfWork.AlertRules.GetEnabledByTypeAsync(
            AlertRuleType.DiscrepancyThreshold, cancellationToken);

        var candidates = rules
            .Where(r => r.AccountId == null || r.AccountId == notification.AccountId)
            .ToList();

        if (candidates.Count == 0)
            return;

        var reconciliation = await _unitOfWork.Reconciliations.GetByIdAsync(
            notification.ReconciliationId, cancellationToken);
        var account = await _unitOfWork.Accounts.GetByIdAsync(
            notification.AccountId, cancellationToken);

        var totalExternal = reconciliation?.TotalExternalAmount ?? 0m;
        var accountName = account?.AccountName ?? notification.AccountId.ToString("N");
        var now = DateTimeOffset.UtcNow;

        foreach (var rule in candidates)
        {
            if (!rule.CanTrigger(now))
                continue;

            var trigger = AlertRuleEvaluator.EvaluateDiscrepancy(
                rule,
                notification.DiscrepancyAmount,
                totalExternal,
                notification.ReconciliationDate,
                accountName);

            if (trigger == null)
                continue;

            var alert = BusinessAlertMapper.ToAlert(rule, trigger, new Dictionary<string, object?>
            {
                ["reconciliationId"] = notification.ReconciliationId,
                ["accountId"] = notification.AccountId,
                ["date"] = notification.ReconciliationDate.ToString("yyyy-MM-dd"),
                ["discrepancyAmount"] = notification.DiscrepancyAmount,
                ["totalExternalAmount"] = totalExternal
            });

            _logger.LogInformation(
                "Regla de negocio {RuleName} disparó ({Severity}) para la rec {ReconciliationId}",
                rule.Name, alert.Severity, notification.ReconciliationId);

            await _dispatcher.DispatchAsync(alert, cancellationToken);

            // Sin SaveChanges: el evento se despacha dentro del SaveChangesAsync
            // del DbContext, que re-guarda los cambios que dejan los handlers.
            rule.MarkTriggered(now);
        }
    }
}
