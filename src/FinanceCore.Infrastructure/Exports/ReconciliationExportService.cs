using System.Text;
using Microsoft.EntityFrameworkCore;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Exports;

public interface IReconciliationExportService
{
    Task WriteDiscrepanciesCsvAsync(Guid reconciliationId, Stream destination, CancellationToken ct = default);
    Task<ReconciliationRangeReport?> BuildRangeReportAsync(
        Guid accountId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record ReconciliationRangeReport(
    Guid AccountId,
    DateOnly From,
    DateOnly To,
    int TotalReconciliations,
    int CompletedClean,
    int CompletedWithDiscrepancies,
    int Failed,
    decimal TotalDiscrepancyAmount,
    int TotalDiscrepancies,
    int TotalApproved,
    IReadOnlyList<ReconciliationRangePoint> Series);

public sealed record ReconciliationRangePoint(
    DateOnly Date,
    string Status,
    int Matched,
    int UnmatchedInternal,
    int UnmatchedExternal,
    decimal DiscrepancyAmount,
    int DiscrepancyCount,
    bool Approved);

public class ReconciliationExportService : IReconciliationExportService
{
    private readonly FinanceCoreDbContext _context;

    public ReconciliationExportService(FinanceCoreDbContext context) => _context = context;

    public async Task WriteDiscrepanciesCsvAsync(Guid reconciliationId, Stream destination, CancellationToken ct = default)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

        await writer.WriteLineAsync(CsvWriter.FormatRow(
            "Id", "ReconciliationId", "Type",
            "InternalTransactionId", "ExternalReference",
            "InternalAmount", "ExternalAmount", "DifferenceAmount",
            "InternalDate", "ExternalDate",
            "IsResolved", "ResolutionType", "ResolvedBy", "ResolvedAt",
            "Notes", "CreatedAt"));

        var query = _context.ReconciliationDiscrepancies
            .AsNoTracking()
            .Where(d => d.ReconciliationId == reconciliationId)
            .OrderBy(d => d.CreatedAt);

        await foreach (var d in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            await writer.WriteLineAsync(CsvWriter.FormatRow(
                d.Id, d.ReconciliationId, d.DiscrepancyType,
                d.InternalTransactionId, d.ExternalReference,
                d.InternalAmount, d.ExternalAmount, d.DifferenceAmount,
                d.InternalDate, d.ExternalDate,
                d.IsResolved, d.ResolutionType, d.ResolvedBy, d.ResolvedAt,
                d.ResolutionNotes, d.CreatedAt));
        }

        await writer.FlushAsync(ct);
    }

    public async Task<ReconciliationRangeReport?> BuildRangeReportAsync(
        Guid accountId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);

        var reconciliations = await _context.Reconciliations
            .AsNoTracking()
            .Include(r => r.Discrepancies)
            .Where(r => r.AccountId == accountId
                     && r.ReconciliationDate >= from
                     && r.ReconciliationDate <= to)
            .OrderBy(r => r.ReconciliationDate)
            .ToListAsync(ct);

        if (reconciliations.Count == 0)
            return null;

        var series = reconciliations.Select(r => new ReconciliationRangePoint(
            Date: r.ReconciliationDate,
            Status: r.Status.ToString(),
            Matched: r.MatchedCount,
            UnmatchedInternal: r.UnmatchedInternal,
            UnmatchedExternal: r.UnmatchedExternal,
            DiscrepancyAmount: r.DiscrepancyAmount,
            DiscrepancyCount: r.Discrepancies.Count,
            Approved: r.ApprovedAt.HasValue)).ToList();

        return new ReconciliationRangeReport(
            AccountId: accountId,
            From: from,
            To: to,
            TotalReconciliations: reconciliations.Count,
            CompletedClean: reconciliations.Count(r => r.Status == Domain.Enums.ReconciliationStatus.Completed),
            CompletedWithDiscrepancies: reconciliations.Count(r => r.Status == Domain.Enums.ReconciliationStatus.CompletedWithDiscrepancies),
            Failed: reconciliations.Count(r => r.Status == Domain.Enums.ReconciliationStatus.Failed),
            TotalDiscrepancyAmount: reconciliations.Sum(r => Math.Abs(r.DiscrepancyAmount)),
            TotalDiscrepancies: reconciliations.Sum(r => r.Discrepancies.Count),
            TotalApproved: reconciliations.Count(r => r.ApprovedAt.HasValue),
            Series: series);
    }
}
