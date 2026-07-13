using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinanceCore.Domain.Repositories;
using FinanceCore.Domain.Services;
using FinanceCore.Infrastructure.BackgroundJobs.Jobs;
using FinanceCore.Infrastructure.Observability;

namespace FinanceCore.Infrastructure.Reconciliations;

public class ReconciliationEngine : IReconciliationEngine
{
    private readonly ILogger<ReconciliationEngine> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ReconciliationOptions _options;
    private readonly ReconciliationMetrics _metrics;

    public ReconciliationEngine(
        ILogger<ReconciliationEngine> logger,
        IUnitOfWork unitOfWork,
        IOptions<ReconciliationOptions> options,
        ReconciliationMetrics metrics)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _metrics = metrics;
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

        var sourceTag = new KeyValuePair<string, object?>("source", source.ToString());
        _metrics.RunsTotal.Add(1, sourceTag);

        using var activity = FinanceCoreTelemetry.ActivitySource.StartActivity(
            "Reconciliation.Execute",
            ActivityKind.Internal);
        activity?.SetTag("financecore.account_ref", accountRef);
        activity?.SetTag("financecore.date", date.ToString("yyyy-MM-dd"));
        activity?.SetTag("financecore.source", source.ToString());

        var stopwatch = Stopwatch.StartNew();

        var existing = await _unitOfWork.Reconciliations.GetByAccountAndDateAsync(accountId, date, ct);
        if (existing != null && existing.Status == Domain.Enums.ReconciliationStatus.Completed)
        {
            _logger.LogInformation(
                "Reconciliation skipped: already completed for account ref {AccountRef} on {Date} (id {Id})",
                accountRef, date, existing.Id);
            return BuildResult(existing);
        }

        // La pasada BalanceOnly nunca degrada una rec que ya
        // tiene resultados de matching: Complete() re-escribiría los totales del
        // extracto con números de balance. El re-run CON extracto si puede
        // re-conciliar una rec con discrepancias.
        if (existing != null
            && source == Domain.Entities.ReconciliationSource.BalanceOnly
            && existing.Status == Domain.Enums.ReconciliationStatus.CompletedWithDiscrepancies)
        {
            _logger.LogInformation(
                "Reconciliation skipped: balance-only pass over existing reconciliation " +
                "with discrepancies for account ref {AccountRef} on {Date} (id {Id})",
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
                var groupContext = await BuildGroupMatchingContextAsync(accountId, date, ct);
                ApplyStatementMatching(reconciliation, postedTransactions, statementLines!, groupContext);
            }
            else
            {
                await ApplyBalanceCheckAsync(reconciliation, accountId, date, postedTransactions, ct);
            }

            await MarkDailyBalanceReconciledAsync(reconciliation, accountId, date, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            stopwatch.Stop();
            _metrics.DurationMs.Record(stopwatch.Elapsed.TotalMilliseconds, sourceTag);
            _metrics.DiscrepancyAmount.Record(Math.Abs((double)reconciliation.DiscrepancyAmount), sourceTag);

            if (reconciliation.Status == Domain.Enums.ReconciliationStatus.Completed)
                _metrics.RunsCompletedClean.Add(1, sourceTag);
            else if (reconciliation.Status == Domain.Enums.ReconciliationStatus.CompletedWithDiscrepancies)
                _metrics.RunsCompletedWithDiscrepancies.Add(1, sourceTag);

            foreach (var group in reconciliation.Discrepancies.GroupBy(d => d.DiscrepancyType))
            {
                _metrics.DiscrepanciesCreated.Add(
                    group.Count(),
                    new KeyValuePair<string, object?>("type", group.Key.ToString()));
            }

            activity?.SetTag("financecore.status", reconciliation.Status.ToString());
            activity?.SetTag("financecore.matched", reconciliation.MatchedCount);
            activity?.SetTag("financecore.discrepancies", reconciliation.Discrepancies.Count);

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
            stopwatch.Stop();
            _metrics.RunsFailed.Add(1, sourceTag);
            _metrics.DurationMs.Record(stopwatch.Elapsed.TotalMilliseconds, sourceTag);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

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

    /// <summary>
    /// Perfiles de fuente activos + transacciones de la ventana extendida que
    /// necesita la pasada N:1. Vacío cuando no hay perfiles configurados.
    /// </summary>
    private sealed record GroupMatchingContext(
        IReadOnlyList<Domain.Entities.ReconciliationSourceProfile> Profiles,
        IReadOnlyList<Domain.Entities.Transaction> WindowTransactions);

    private async Task<GroupMatchingContext> BuildGroupMatchingContextAsync(
        Guid accountId,
        DateOnly date,
        CancellationToken ct)
    {
        var profiles = await _unitOfWork.SourceProfiles.GetActiveForAccountAsync(accountId, ct);
        if (profiles.Count == 0)
        {
            return new GroupMatchingContext(profiles, Array.Empty<Domain.Entities.Transaction>());
        }

        var maxWindowDays = profiles.Max(p => p.GroupingWindowDays);
        var windowTransactions = await _unitOfWork.Transactions.GetByAccountAndDateRangeAsync(
            accountId, date.AddDays(-maxWindowDays), date, ct);

        // Sólo Posted: las Reconciled ya fueron consumidas (1:1 o grupo previo).
        return new GroupMatchingContext(
            profiles,
            windowTransactions.Where(t => t.Status == Domain.Enums.TransactionStatus.Posted).ToList());
    }

    private void ApplyStatementMatching(
        Domain.Entities.Reconciliation reconciliation,
        IReadOnlyList<Domain.Entities.Transaction> postedTransactions,
        IReadOnlyList<Domain.Entities.ExternalStatementLine> statementLines,
        GroupMatchingContext groupContext)
    {
        // Transacciones consumidas por grupos de una corrida anterior (re-run):
        // miembros y fees quedan fuera del 1:1 y del conteo base — su aporte a
        // los totales entra por el agregado del grupo (neto = payout).
        var preGroupedIds = reconciliation.MatchGroups
            .SelectMany(g => g.Items.Select(i => i.TransactionId))
            .Concat(reconciliation.MatchGroups
                .Where(g => g.FeeTransactionId.HasValue)
                .Select(g => g.FeeTransactionId!.Value))
            .ToHashSet();

        var preExistingGroupsByRef = reconciliation.MatchGroups
            .ToDictionary(g => g.ExternalReference, StringComparer.OrdinalIgnoreCase);

        var internalPool = postedTransactions.Where(t => !preGroupedIds.Contains(t.Id)).ToList();
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

        // ---- Pasada N:1 (SCRUM-41): payouts de pasarela agrupados ----------
        // Cada grupo aporta a los totales su NETO (= payout): las ventas brutas
        // y el fee generado se compensan, así un extracto totalmente agrupado
        // cierra limpio contra el banco.
        var groupedExternalIndexes = new HashSet<int>();
        var newlyGroupedIds = new HashSet<Guid>();
        var nearMissNotes = new Dictionary<int, string>();
        var groupsInternalCount = 0;
        var groupsPayoutTotal = 0m;

        for (var i = 0; i < statementLines.Count; i++)
        {
            if (matchedExternalIndexes.Contains(i)) continue;
            var ext = statementLines[i];

            // Re-run idempotente: un payout que ya formó grupo en esta rec
            // cuenta como matcheado y no se re-forma ni re-toca nada.
            if (preExistingGroupsByRef.TryGetValue(ext.ExternalReference, out var existingGroup))
            {
                groupedExternalIndexes.Add(i);
                groupsInternalCount += existingGroup.GroupedCount;
                groupsPayoutTotal += existingGroup.PayoutAmount;
                matched += existingGroup.GroupedCount;
                continue;
            }

            if (ext.Amount <= 0 || groupContext.Profiles.Count == 0) continue;

            var profile = groupContext.Profiles.FirstOrDefault(p => PayoutMatches(p, ext));
            if (profile == null) continue;

            var windowStart = ext.ValueDate.AddDays(-profile.GroupingWindowDays);
            var pool = groupContext.WindowTransactions
                .Where(t => !matchedInternal.Contains(t.Id)
                         && !newlyGroupedIds.Contains(t.Id)
                         && !preGroupedIds.Contains(t.Id)
                         && t.ValueDate >= windowStart
                         && t.ValueDate <= ext.ValueDate
                         && InternalMatches(profile, t))
                .ToList();

            var result = GroupMatcher.FindGroup(
                ext.Amount,
                ext.ValueDate,
                pool.Select(t => new GroupCandidate(t.Id, t.Amount.Amount, t.ValueDate)).ToList(),
                profile.ExpectedFeePercent,
                profile.FeeTolerancePercent);

            if (result.Match is { } proposal)
            {
                var group = reconciliation.AddMatchGroup(
                    profile.Id,
                    ext.ExternalReference,
                    ext.Amount,
                    ext.ValueDate,
                    proposal.Items.Select(c => (c.TransactionId, c.Amount)).ToList(),
                    proposal.WindowStart,
                    proposal.WindowEnd);

                // Fee explícito: la comisión queda visible contablemente y
                // ventas − fee = payout, los libros cierran exactos.
                if (proposal.FeeAmount > 0)
                {
                    var fee = Domain.Entities.Transaction.CreateFee(
                        $"groupfee-{group.Id:N}",
                        "SYSTEM",
                        reconciliation.AccountId,
                        proposal.FeeAmount,
                        ext.CurrencyCode,
                        ext.ValueDate,
                        $"Comisión {profile.DisplayName} · payout {ext.ExternalReference}");
                    fee.StartProcessing();
                    fee.MarkAsValidated();
                    fee.Post();
                    _unitOfWork.Transactions.Add(fee);
                    group.AttachFeeTransaction(fee.Id);
                }

                // Las ventas quedan conciliadas contra la rec del payout y no
                // vuelven a entrar en pools futuros.
                var memberIds = proposal.Items.Select(c => c.TransactionId).ToHashSet();
                foreach (var t in pool.Where(t => memberIds.Contains(t.Id)))
                    t.Reconcile(reconciliation.Id);

                newlyGroupedIds.UnionWith(memberIds);
                groupedExternalIndexes.Add(i);
                groupsInternalCount += group.GroupedCount;
                groupsPayoutTotal += group.PayoutAmount;
                matched += group.GroupedCount;

                _logger.LogInformation(
                    "Group match: payout {Reference} ({Payout}) = {Count} txns ({Gross}) - fee {Fee} ({FeePct:P2})",
                    ext.ExternalReference, ext.Amount, group.GroupedCount,
                    group.GroupedAmount, group.FeeAmount, group.FeePercent);
            }
            else if (result.NearMissNote != null)
            {
                nearMissNotes[i] = result.NearMissNote;
            }
        }

        // ---- No matcheados -------------------------------------------------
        var unmatchedExternal = 0;
        for (var i = 0; i < statementLines.Count; i++)
        {
            if (matchedExternalIndexes.Contains(i) || groupedExternalIndexes.Contains(i)) continue;
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
                notes: nearMissNotes.TryGetValue(i, out var nearMiss)
                    ? nearMiss
                    : "Transacción presente en extracto externo, ausente internamente");
        }

        var unmatchedInternal = 0;
        foreach (var t in internalPool)
        {
            if (matchedInternal.Contains(t.Id) || newlyGroupedIds.Contains(t.Id)) continue;
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

        // ---- Totales -------------------------------------------------------
        // Base = transacciones del día no consumidas por grupos; cada grupo
        // suma sus N ventas al conteo y su payout (neto) a los montos.
        var baseTransactions = internalPool.Where(t => !newlyGroupedIds.Contains(t.Id)).ToList();
        var totalInternalRecords = baseTransactions.Count + groupsInternalCount;
        var totalInternalAmount = baseTransactions.Sum(t => t.Amount.Amount) + groupsPayoutTotal;
        var totalExternalAmount = statementLines.Sum(l => l.Amount);
        var discrepancyAmount = totalExternalAmount - totalInternalAmount;

        reconciliation.Complete(
            totalInternal: totalInternalRecords,
            totalExternal: statementLines.Count,
            matched: matched,
            unmatchedInternal: unmatchedInternal,
            unmatchedExternal: unmatchedExternal,
            totalInternalAmount: totalInternalAmount,
            totalExternalAmount: totalExternalAmount,
            discrepancyAmount: discrepancyAmount);
    }

    private static bool PayoutMatches(
        Domain.Entities.ReconciliationSourceProfile profile,
        Domain.Entities.ExternalStatementLine line)
        => SafeIsMatch($"{line.ExternalReference} {line.Description}", profile.PayoutPattern);

    private static bool InternalMatches(
        Domain.Entities.ReconciliationSourceProfile profile,
        Domain.Entities.Transaction transaction)
    {
        var value = profile.InternalMatchField switch
        {
            Domain.Enums.InternalMatchField.ExternalIdSource => transaction.ExternalIdSource,
            Domain.Enums.InternalMatchField.Category => transaction.Category,
            Domain.Enums.InternalMatchField.CounterpartyName => transaction.Counterparty?.Name,
            _ => null
        };

        return value != null && SafeIsMatch(value, profile.InternalMatchPattern);
    }

    private static bool SafeIsMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(
                input,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            // Patrón patológico: mejor no matchear que colgar la conciliación.
            return false;
        }
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
