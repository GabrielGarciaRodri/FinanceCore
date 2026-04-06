using MediatR;
using Microsoft.Extensions.Logging;
using FinanceCore.Domain.Events;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.Application.DomainEvents;

/// <summary>
/// Handler para TransactionCreatedEvent.
/// Registra la creación y puede disparar procesamiento adicional.
/// </summary>
public class TransactionCreatedEventHandler : INotificationHandler<TransactionCreatedEvent>
{
    private readonly ILogger<TransactionCreatedEventHandler> _logger;

    public TransactionCreatedEventHandler(ILogger<TransactionCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(TransactionCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[DomainEvent] TransactionCreated - Id: {TransactionId}, ExternalId: {ExternalId}, " +
            "Account: {AccountId}, Amount: {Amount} {Currency}, ValueDate: {ValueDate}",
            notification.TransactionId,
            notification.ExternalId,
            notification.AccountId,
            notification.Amount,
            notification.CurrencyCode,
            notification.ValueDate);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Handler para TransactionPostedEvent.
/// Actualiza saldos de cuenta cuando una transacción se contabiliza.
/// </summary>
public class TransactionPostedEventHandler : INotificationHandler<TransactionPostedEvent>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TransactionPostedEventHandler> _logger;

    public TransactionPostedEventHandler(
        IUnitOfWork unitOfWork,
        ILogger<TransactionPostedEventHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Handle(TransactionPostedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[DomainEvent] TransactionPosted - Id: {TransactionId}, Account: {AccountId}, " +
            "Amount: {Amount} {Currency}",
            notification.TransactionId,
            notification.AccountId,
            notification.Amount,
            notification.CurrencyCode);

        var account = await _unitOfWork.Accounts.GetByIdAsync(notification.AccountId, cancellationToken);
        if (account == null)
        {
            _logger.LogWarning(
                "[DomainEvent] TransactionPosted - Account {AccountId} not found, skipping balance update",
                notification.AccountId);
            return;
        }

        var transaction = await _unitOfWork.Transactions.GetByIdAsync(notification.TransactionId, cancellationToken);
        if (transaction == null)
        {
            _logger.LogWarning(
                "[DomainEvent] TransactionPosted - Transaction {TransactionId} not found",
                notification.TransactionId);
            return;
        }

        try
        {
            account.ApplyTransaction(transaction);
            _unitOfWork.Accounts.Update(account);

            _logger.LogInformation(
                "[DomainEvent] TransactionPosted - Balance updated for account {AccountId}. " +
                "New balance: {Balance} {Currency}",
                notification.AccountId,
                account.CurrentBalance.Amount,
                account.CurrentBalance.Currency.Code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[DomainEvent] TransactionPosted - Failed to update balance for account {AccountId}",
                notification.AccountId);
        }
    }
}

/// <summary>
/// Handler para ReconciliationCompletedEvent.
/// Registra resultados y alerta si hay discrepancias significativas.
/// </summary>
public class ReconciliationCompletedEventHandler : INotificationHandler<ReconciliationCompletedEvent>
{
    private readonly ILogger<ReconciliationCompletedEventHandler> _logger;

    public ReconciliationCompletedEventHandler(ILogger<ReconciliationCompletedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(ReconciliationCompletedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[DomainEvent] ReconciliationCompleted - Id: {ReconciliationId}, Account: {AccountId}, " +
            "Date: {Date}, Matched: {Matched}, Unmatched: {Unmatched}, Discrepancy: {Discrepancy}",
            notification.ReconciliationId,
            notification.AccountId,
            notification.ReconciliationDate,
            notification.MatchedCount,
            notification.UnmatchedCount,
            notification.DiscrepancyAmount);

        if (notification.HasDiscrepancies)
        {
            _logger.LogWarning(
                "[DomainEvent] ReconciliationCompleted - DISCREPANCIES DETECTED for account {AccountId}. " +
                "Unmatched: {Unmatched}, Amount: {Amount}",
                notification.AccountId,
                notification.UnmatchedCount,
                notification.DiscrepancyAmount);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Handler para AccountBalanceChangedEvent.
/// Detecta cambios significativos en saldos.
/// </summary>
public class AccountBalanceChangedEventHandler : INotificationHandler<AccountBalanceChangedEvent>
{
    private readonly ILogger<AccountBalanceChangedEventHandler> _logger;

    public AccountBalanceChangedEventHandler(ILogger<AccountBalanceChangedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(AccountBalanceChangedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[DomainEvent] AccountBalanceChanged - Account: {AccountId}, " +
            "Previous: {Previous}, New: {New}, Change: {Change} {Currency}",
            notification.AccountId,
            notification.PreviousBalance,
            notification.NewBalance,
            notification.Change,
            notification.CurrencyCode);

        // Alert on large balance changes (threshold: 100,000 in any currency)
        if (Math.Abs(notification.Change) > 100_000)
        {
            _logger.LogWarning(
                "[DomainEvent] AccountBalanceChanged - LARGE BALANCE CHANGE detected. " +
                "Account: {AccountId}, Change: {Change} {Currency}",
                notification.AccountId,
                notification.Change,
                notification.CurrencyCode);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Handler para DuplicateTransactionDetectedEvent.
/// Registra intentos de duplicación para auditoría.
/// </summary>
public class DuplicateTransactionDetectedEventHandler : INotificationHandler<DuplicateTransactionDetectedEvent>
{
    private readonly ILogger<DuplicateTransactionDetectedEventHandler> _logger;

    public DuplicateTransactionDetectedEventHandler(ILogger<DuplicateTransactionDetectedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(DuplicateTransactionDetectedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "[DomainEvent] DuplicateTransactionDetected - ExternalId: {ExternalId}, " +
            "Source: {Source}, ExistingId: {ExistingId}, Hash: {Hash}",
            notification.ExternalId,
            notification.Source,
            notification.ExistingTransactionId,
            notification.Hash);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Handler para FinancialAnomalyDetectedEvent.
/// Logs anomalies with severity-based alerting.
/// </summary>
public class FinancialAnomalyDetectedEventHandler : INotificationHandler<FinancialAnomalyDetectedEvent>
{
    private readonly ILogger<FinancialAnomalyDetectedEventHandler> _logger;

    public FinancialAnomalyDetectedEventHandler(ILogger<FinancialAnomalyDetectedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(FinancialAnomalyDetectedEvent notification, CancellationToken cancellationToken)
    {
        var logLevel = notification.Severity switch
        {
            "Critical" => LogLevel.Critical,
            "High" => LogLevel.Error,
            "Medium" => LogLevel.Warning,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel,
            "[DomainEvent] FinancialAnomaly - Type: {AnomalyType}, Severity: {Severity}, " +
            "Description: {Description}, Account: {AccountId}, Transaction: {TransactionId}",
            notification.AnomalyType,
            notification.Severity,
            notification.Description,
            notification.AccountId,
            notification.TransactionId);

        return Task.CompletedTask;
    }
}
