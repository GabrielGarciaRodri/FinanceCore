using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.BackgroundJobs.Jobs;

namespace FinanceCore.Infrastructure.Reconciliations;

public class ReconciliationEngine : IReconciliationEngine
{
    private readonly ILogger<ReconciliationEngine> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ReconciliationOptions _options;

    public ReconciliationEngine(
        ILogger<ReconciliationEngine> logger,
        IUnitOfWork unitOfWork,
        IOptions<ReconciliationOptions> options)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public Task<ReconciliationResult> ReconcileAsync(Guid accountId, DateOnly date, CancellationToken ct = default)
        => ExecuteAsync(accountId, date, Domain.Entities.ReconciliationSource.BalanceOnly, statementLines: null, ct);

    public Task<ReconciliationResult> ReconcileWithStatementAsync(
        Guid accountId,
        DateOnly date,
        IReadOnlyList<Domain.Entities.ExternalStatementLine> statementLines,
        CancellationToken ct = default)
    {
        if (statementLines == null)
            throw new ArgumentNullException(nameof(statementLines));

        return ExecuteAsync(accountId, date, Domain.Entities.ReconciliationSource.StatementTransactions, statementLines, ct);
    }

    private async Task<ReconciliationResult> ExecuteAsync(
        Guid accountId,
        DateOnly date,
        Domain.Entities.ReconciliationSource source,
        IReadOnlyList<Domain.Entities.ExternalStatementLine>? statementLines,
        CancellationToken ct)
    {
        var accountRef = accountId.ToString("N")[..8];
        _logger.LogInformation(
            "Reconciling account ref {AccountRef} for {Date} using {Source}",
            accountRef, date, source);

        var existing = await _unitOfWork.Reconciliations.GetByAccountAndDateAsync(accountId, date, ct);
        if (existing != null && existing.Status == Domain.Enums.ReconciliationStatus.Completed)
        {
            _logger.LogInformation(
                "Reconciliation skipped: already completed for account ref {AccountRef} on {Date} (id {Id})",
                accountRef, date, existing.Id);
            return BuildResult(existing);
        }

        var reconciliation = existing ?? Domain.Entities.Reconciliation.Start(
            accountId,
            date,
            _options.SystemProcessor,
            notes: $"Auto reconciliation ({source})");

        if (existing == null)
            _unitOfWork.Reconciliations.Add(reconciliation);

        try
        {
            var transactions = await _unitOfWork.Transactions.GetByAccountAndDateRangeAsync(accountId, date, date, ct);
            var postedTransactions = transactions
                .Where(t => t.Status == Domain.Enums.TransactionStatus.Posted
                         || t.Status == Domain.Enums.TransactionStatus.Reconciled)
                .ToList();

            DetectDuplicates(reconciliation, postedTransactions);

            if (source == Domain.Entities.ReconciliationSource.StatementTransactions)
            {
                ApplyStatementMatching(reconciliation, postedTransactions, statementLines!);
            }
            else
            {
                await ApplyBalanceCheckAsync(reconciliation, accountId, date, postedTransactions, ct);
            }

            await MarkDailyBalanceReconciledAsync(reconciliation, accountId, date, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Reconciliation persisted. Id={Id}, Status={Status}, Matched={Matched}, " +
                "UnmatchedInternal={UI}, UnmatchedExternal={UE}, Discrepancy={Discrepancy}, Count={Count}",
                reconciliation.Id,
                reconciliation.Status,
                reconciliation.MatchedCount,
                reconciliation.UnmatchedInternal,
                reconciliation.UnmatchedExternal,
                reconciliation.DiscrepancyAmount,
                reconciliation.Discrepancies.Count);

            return BuildResult(reconciliation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconciliation failed for account ref {AccountRef} on {Date}", accountRef, date);
            reconciliation.Fail(ex.Message);
            try { await _unitOfWork.SaveChangesAsync(ct); } catch { /* swallow persistence error on failure path */ }
            throw;
        }
    }

    private void DetectDuplicates(
        Domain.Entities.Reconciliation reconciliation,
        IReadOnlyList<Domain.Entities.Transaction> postedTransactions)
    {
        var duplicateGroups = postedTransactions
            .GroupBy(t => t.ExternalId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            var sorted = group.OrderBy(t => t.CreatedAt).ToList();
            foreach (var dup in sorted.Skip(1))
            {
                reconciliation.AddDiscrepancy(
                    Domain.Enums.DiscrepancyType.PossibleDuplicate,
                    internalTransactionId: dup.Id,
                    externalReference: dup.ExternalId,
                    internalAmount: dup.Amount.Amount,
                    externalAmount: null,
                    internalDate: dup.ValueDate,
                    externalDate: null,
                    notes: $"Duplicado de transacción {sorted[0].Id}");
            }
        }
    }

    private async Task ApplyBalanceCheckAsync(
        Domain.Entities.Reconciliation reconciliation,
        Guid accountId,
        DateOnly date,
        IReadOnlyList<Domain.Entities.Transaction> postedTransactions,
        CancellationToken ct)
    {
        var dailyBalance = await _unitOfWork.DailyBalances.GetByAccountAndDateAsync(accountId, date, ct);
        var previousBalance = await _unitOfWork.DailyBalances.GetByAccountAndDateAsync(accountId, date.AddDays(-1), ct);

        var openingBalance = previousBalance?.ClosingBalance ?? dailyBalance?.OpeningBalance ?? 0m;
        var postedAmount = postedTransactions.Sum(t => t.Amount.Amount);
        var expectedClosing = openingBalance + postedAmount;
        var actualClosing = dailyBalance?.ClosingBalance ?? expectedClosing;
        var discrepancyAmount = actualClosing - expectedClosing;

        if (Math.Abs(discrepancyAmount) > _options.BalanceTolerance)
        {
            reconciliation.AddDiscrepancy(
                Domain.Enums.DiscrepancyType.AmountMismatch,
                internalTransactionId: null,
                externalReference: $"ClosingBalance@{date:yyyy-MM-dd}",
                internalAmount: expectedClosing,
                externalAmount: actualClosing,
                internalDate: date,
                externalDate: date,
                notes: $"Diferencia balance: esperado {expectedClosing}, reportado {actualClosing}");
        }

        var duplicateCount = reconciliation.Discrepancies.Count(d =>
            d.DiscrepancyType == Domain.Enums.DiscrepancyType.PossibleDuplicate);

        var matched = Math.Max(0, postedTransactions.Count - duplicateCount);

        reconciliation.Complete(
            totalInternal: postedTransactions.Count,
            totalExternal: dailyBalance != null ? postedTransactions.Count : 0,
            matched: matched,
            unmatchedInternal: duplicateCount,
            unmatchedExternal: 0,
            totalInternalAmount: postedAmount,
            totalExternalAmount: actualClosing - openingBalance,
            discrepancyAmount: discrepancyAmount);
    }

    private void ApplyStatementMatching(
        Domain.Entities.Reconciliation reconciliation,
        IReadOnlyList<Domain.Entities.Transaction> postedTransactions,
        IReadOnlyList<Domain.Entities.ExternalStatementLine> statementLines)
    {
        var internalPool = postedTransactions.ToList();
        var matchedInternal = new HashSet<Guid>();
        var matchedExternalIndexes = new HashSet<int>();
        var matched = 0;

        for (var i = 0; i < statementLines.Count; i++)
        {
            var ext = statementLines[i];
            var candidate = internalPool.FirstOrDefault(t =>
                !matchedInternal.Contains(t.Id) &&
                string.Equals(t.Amount.Currency.Code, ext.CurrencyCode, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(t.Amount.Amount - ext.Amount) <= _options.AmountTolerance &&
                Math.Abs((t.ValueDate.DayNumber - ext.ValueDate.DayNumber)) <= _options.DateToleranceDays &&
                (string.Equals(t.ExternalId, ext.ExternalReference, StringComparison.OrdinalIgnoreCase) ||
                 _options.DateToleranceDays >= 0));

            if (candidate != null)
            {
                matchedInternal.Add(candidate.Id);
                matchedExternalIndexes.Add(i);
                matched++;

                if (!string.Equals(candidate.ExternalId, ext.ExternalReference, StringComparison.OrdinalIgnoreCase))
                {
                    reconciliation.AddDiscrepancy(
                        Domain.Enums.DiscrepancyType.ReferenceMismatch,
                        internalTransactionId: candidate.Id,
                        externalReference: ext.ExternalReference,
                        internalAmount: candidate.Amount.Amount,
                        externalAmount: ext.Amount,
                        internalDate: candidate.ValueDate,
                        externalDate: ext.ValueDate,
                        notes: $"Referencia interna {candidate.ExternalId} no coincide con externa {ext.ExternalReference}");
                }

                if (candidate.ValueDate != ext.ValueDate)
                {
                    reconciliation.AddDiscrepancy(
                        Domain.Enums.DiscrepancyType.DateMismatch,
                        internalTransactionId: candidate.Id,
                        externalReference: ext.ExternalReference,
                        internalAmount: candidate.Amount.Amount,
                        externalAmount: ext.Amount,
                        internalDate: candidate.ValueDate,
                        externalDate: ext.ValueDate,
                        notes: $"Diferencia de fecha (tolerancia {_options.DateToleranceDays} días)");
                }
            }
        }

        var unmatchedExternal = 0;
        for (var i = 0; i < statementLines.Count; i++)
        {
            if (matchedExternalIndexes.Contains(i)) continue;
            var ext = statementLines[i];
            unmatchedExternal++;
            reconciliation.AddDiscrepancy(
                Domain.Enums.DiscrepancyType.MissingInternal,
                internalTransactionId: null,
                externalReference: ext.ExternalReference,
                internalAmount: null,
                externalAmount: ext.Amount,
                internalDate: null,
                externalDate: ext.ValueDate,
                notes: "Transacción presente en extracto externo, ausente internamente");
        }

        var unmatchedInternal = 0;
        foreach (var t in postedTransactions)
        {
            if (matchedInternal.Contains(t.Id)) continue;
            unmatchedInternal++;
            reconciliation.AddDiscrepancy(
                Domain.Enums.DiscrepancyType.MissingExternal,
                internalTransactionId: t.Id,
                externalReference: null,
                internalAmount: t.Amount.Amount,
                externalAmount: null,
                internalDate: t.ValueDate,
                externalDate: null,
                notes: "Transacción interna sin contraparte en extracto externo");
        }

        var totalInternalAmount = postedTransactions.Sum(t => t.Amount.Amount);
        var totalExternalAmount = statementLines.Sum(l => l.Amount);
        var discrepancyAmount = totalExternalAmount - totalInternalAmount;

        reconciliation.Complete(
            totalInternal: postedTransactions.Count,
            totalExternal: statementLines.Count,
            matched: matched,
            unmatchedInternal: unmatchedInternal,
            unmatchedExternal: unmatchedExternal,
            totalInternalAmount: totalInternalAmount,
            totalExternalAmount: totalExternalAmount,
            discrepancyAmount: discrepancyAmount);
    }

    private async Task MarkDailyBalanceReconciledAsync(
        Domain.Entities.Reconciliation reconciliation,
        Guid accountId,
        DateOnly date,
        CancellationToken ct)
    {
        if (reconciliation.Status != Domain.Enums.ReconciliationStatus.Completed)
            return;

        var dailyBalance = await _unitOfWork.DailyBalances.GetByAccountAndDateAsync(accountId, date, ct);
        if (dailyBalance == null) return;

        dailyBalance.IsReconciled = true;
        dailyBalance.ReconciledAt = DateTimeOffset.UtcNow;
        dailyBalance.ReconciledBy = _options.SystemProcessor;
        // EF change tracker captura los cambios automáticamente al SaveChanges.
    }

    private static ReconciliationResult BuildResult(Domain.Entities.Reconciliation reconciliation)
    {
        var hasDiscrepancies = reconciliation.Status == Domain.Enums.ReconciliationStatus.CompletedWithDiscrepancies
            || reconciliation.Discrepancies.Count > 0
            || reconciliation.UnmatchedInternal > 0
            || reconciliation.UnmatchedExternal > 0;

        return new ReconciliationResult(
            ReconciliationId: reconciliation.Id,
            MatchedCount: reconciliation.MatchedCount,
            UnmatchedInternal: reconciliation.UnmatchedInternal,
            UnmatchedExternal: reconciliation.UnmatchedExternal,
            DiscrepancyAmount: reconciliation.DiscrepancyAmount,
            HasDiscrepancies: hasDiscrepancies,
            Status: reconciliation.Status,
            DiscrepancyCount: reconciliation.Discrepancies.Count);
    }
}
