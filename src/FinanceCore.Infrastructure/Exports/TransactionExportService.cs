using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Exports;

public interface ITransactionExportService
{
    Task WriteCsvAsync(TransactionSearchCriteria criteria, Stream destination, CancellationToken ct = default);
    Task<byte[]> WriteXlsxAsync(TransactionSearchCriteria criteria, CancellationToken ct = default);
}

public class TransactionExportService : ITransactionExportService
{
    private readonly FinanceCoreDbContext _context;

    public TransactionExportService(FinanceCoreDbContext context) => _context = context;

    public async Task WriteCsvAsync(TransactionSearchCriteria criteria, Stream destination, CancellationToken ct = default)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

        await writer.WriteLineAsync(CsvWriter.FormatRow(
            "Id", "ExternalId", "AccountId", "Type", "Status", "Amount", "Currency",
            "ValueDate", "BookingDate", "Description", "Category",
            "CounterpartyName", "ReconciliationId", "CreatedAt"));

        var query = BuildQuery(criteria);

        await foreach (var t in query.AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
        {
            await writer.WriteLineAsync(CsvWriter.FormatRow(
                t.Id, t.ExternalId, t.AccountId, t.Type, t.Status,
                t.Amount.Amount, t.Amount.Currency.Code,
                t.ValueDate, t.BookingDate, t.Description, t.Category,
                t.Counterparty?.Name, t.ReconciliationId, t.CreatedAt));
        }

        await writer.FlushAsync(ct);
    }

    public async Task<byte[]> WriteXlsxAsync(TransactionSearchCriteria criteria, CancellationToken ct = default)
    {
        var query = BuildQuery(criteria);
        var rows = await query.AsNoTracking().ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Transactions");

        // Header
        var headers = new[]
        {
            "Id", "ExternalId", "AccountId", "Type", "Status", "Amount", "Currency",
            "ValueDate", "BookingDate", "Description", "Category",
            "CounterpartyName", "ReconciliationId", "CreatedAt"
        };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);

        // Body
        for (var i = 0; i < rows.Count; i++)
        {
            var t = rows[i];
            var r = i + 2;
            ws.Cell(r, 1).Value = t.Id.ToString();
            ws.Cell(r, 2).Value = t.ExternalId;
            ws.Cell(r, 3).Value = t.AccountId.ToString();
            ws.Cell(r, 4).Value = t.Type.ToString();
            ws.Cell(r, 5).Value = t.Status.ToString();
            ws.Cell(r, 6).Value = t.Amount.Amount;
            ws.Cell(r, 6).Style.NumberFormat.Format = "#,##0.0000";
            ws.Cell(r, 7).Value = t.Amount.Currency.Code;
            ws.Cell(r, 8).Value = t.ValueDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, 8).Style.DateFormat.Format = "yyyy-mm-dd";
            ws.Cell(r, 9).Value = t.BookingDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, 9).Style.DateFormat.Format = "yyyy-mm-dd";
            ws.Cell(r, 10).Value = t.Description;
            ws.Cell(r, 11).Value = t.Category;
            ws.Cell(r, 12).Value = t.Counterparty?.Name;
            ws.Cell(r, 13).Value = t.ReconciliationId?.ToString();
            ws.Cell(r, 14).Value = t.CreatedAt.UtcDateTime;
            ws.Cell(r, 14).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private IQueryable<Domain.Entities.Transaction> BuildQuery(TransactionSearchCriteria criteria)
    {
        var query = _context.Transactions.AsQueryable();

        if (criteria.AccountId.HasValue)
            query = query.Where(t => t.AccountId == criteria.AccountId.Value);
        if (criteria.StartDate.HasValue)
            query = query.Where(t => t.ValueDate >= criteria.StartDate.Value);
        if (criteria.EndDate.HasValue)
            query = query.Where(t => t.ValueDate <= criteria.EndDate.Value);
        if (criteria.Type.HasValue)
            query = query.Where(t => t.Type == criteria.Type.Value);
        if (criteria.Status.HasValue)
            query = query.Where(t => t.Status == criteria.Status.Value);
        if (!string.IsNullOrWhiteSpace(criteria.Category))
            query = query.Where(t => t.Category == criteria.Category);
        if (!string.IsNullOrWhiteSpace(criteria.SearchText))
            query = query.Where(t => t.Description != null && t.Description.Contains(criteria.SearchText));
        if (criteria.MinAmount.HasValue)
            query = query.Where(t => EF.Property<decimal>(t, "_amountValue") >= criteria.MinAmount.Value);
        if (criteria.MaxAmount.HasValue)
            query = query.Where(t => EF.Property<decimal>(t, "_amountValue") <= criteria.MaxAmount.Value);

        return query.OrderBy(t => t.ValueDate).ThenBy(t => t.CreatedAt);
    }
}
