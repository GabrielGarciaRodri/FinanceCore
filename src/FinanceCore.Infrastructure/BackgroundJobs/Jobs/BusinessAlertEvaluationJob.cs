using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;
using FinanceCore.Domain.Services;
using FinanceCore.Infrastructure.Alerting;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.BackgroundJobs.Jobs;

/// <summary>
/// Evaluación diaria de reglas de alerta de negocio (SCRUM-45) que no están
/// atadas a un evento: payout esperado que no llegó y saldo bajo umbral.
/// Las de discrepancia se evalúan al completar cada conciliación
/// (BusinessRuleAlertHandler).
/// </summary>
public class BusinessAlertEvaluationJob
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly FinanceCoreDbContext _context;
    private readonly IAlertDispatcher _dispatcher;
    private readonly ILogger<BusinessAlertEvaluationJob> _logger;

    public BusinessAlertEvaluationJob(
        IUnitOfWork unitOfWork,
        FinanceCoreDbContext context,
        IAlertDispatcher dispatcher,
        ILogger<BusinessAlertEvaluationJob> logger)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 300, 1800 })]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var triggered = 0;

        _logger.LogInformation("[Job:{JobId}] Evaluando reglas de alerta de negocio", jobId);

        triggered += await EvaluateMissingPayoutsAsync(now, today, cancellationToken);
        triggered += await EvaluateLowBalancesAsync(now, cancellationToken);

        if (triggered > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Job:{JobId}] Fin evaluación. Disparos={Triggered}", jobId, triggered);
    }

    private async Task<int> EvaluateMissingPayoutsAsync(
        DateTimeOffset now,
        DateOnly today,
        CancellationToken ct)
    {
        var rules = await _unitOfWork.AlertRules.GetEnabledByTypeAsync(
            AlertRuleType.MissingPayout, ct);
        var triggered = 0;

        foreach (var rule in rules.Where(r => r.CanTrigger(now)))
        {
            var profile = await _unitOfWork.SourceProfiles.GetByIdAsync(rule.SourceProfileId!.Value, ct);
            if (profile == null || !profile.IsActive)
            {
                _logger.LogWarning(
                    "Regla {RuleName}: el perfil de fuente {ProfileId} no existe o está inactivo",
                    rule.Name, rule.SourceProfileId);
                continue;
            }

            // Último payout conocido de la fuente = grupo N:1 más reciente.
            var lastPayoutDate = await _context.ReconciliationMatchGroups
                .Where(g => g.SourceProfileId == profile.Id)
                .MaxAsync(g => (DateOnly?)g.PayoutDate, ct);

            var trigger = AlertRuleEvaluator.EvaluateMissingPayout(
                rule, lastPayoutDate, today, profile.DisplayName);

            if (trigger == null)
                continue;

            var alert = BusinessAlertMapper.ToAlert(rule, trigger, new Dictionary<string, object?>
            {
                ["sourceProfileId"] = profile.Id,
                ["sourceKey"] = profile.SourceKey,
                ["lastPayoutDate"] = lastPayoutDate?.ToString("yyyy-MM-dd"),
                ["lookbackDays"] = rule.LookbackDays
            });

            await _dispatcher.DispatchAsync(alert, ct);
            rule.MarkTriggered(now);
            triggered++;
        }

        return triggered;
    }

    private async Task<int> EvaluateLowBalancesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var rules = await _unitOfWork.AlertRules.GetEnabledByTypeAsync(
            AlertRuleType.LowBalance, ct);
        var triggered = 0;

        foreach (var rule in rules.Where(r => r.CanTrigger(now)))
        {
            var account = await _unitOfWork.Accounts.GetByIdAsync(rule.AccountId!.Value, ct);
            if (account == null)
            {
                _logger.LogWarning(
                    "Regla {RuleName}: la cuenta {AccountId} no existe", rule.Name, rule.AccountId);
                continue;
            }

            var trigger = AlertRuleEvaluator.EvaluateLowBalance(
                rule,
                account.CurrentBalance.Amount,
                account.Currency.Code,
                account.AccountName);

            if (trigger == null)
                continue;

            var alert = BusinessAlertMapper.ToAlert(rule, trigger, new Dictionary<string, object?>
            {
                ["accountId"] = account.Id,
                ["currentBalance"] = account.CurrentBalance.Amount,
                ["currency"] = account.Currency.Code,
                ["threshold"] = rule.ThresholdAmount
            });

            await _dispatcher.DispatchAsync(alert, ct);
            rule.MarkTriggered(now);
            triggered++;
        }

        return triggered;
    }
}
