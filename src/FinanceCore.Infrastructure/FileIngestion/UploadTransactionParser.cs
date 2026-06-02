using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FinanceCore.Application.Transactions.Commands.IngestTransactions;

namespace FinanceCore.Infrastructure.FileIngestion;

public sealed record TransactionRowError(int Row, string Error);

public sealed record UploadParseResult(
    IReadOnlyList<TransactionDto> Transactions,
    IReadOnlyList<TransactionRowError> Errors);

public enum UploadFormat
{
    Csv,
    Xlsx
}

public interface IUploadTransactionParser
{
    /// <summary>
    /// Parsea un archivo CSV/XLSX y devuelve transacciones válidas + lista de errores por fila.
    /// </summary>
    /// <param name="stream">Stream del archivo (no se cierra).</param>
    /// <param name="format">Formato del archivo.</param>
    /// <param name="accountIdOverride">
    /// Si se provee, sobreescribe el AccountId de todas las filas y emite una entrada
    /// en Errors por cada fila cuyo AccountId del archivo difiera (como warning informativo).
    /// </param>
    Task<UploadParseResult> ParseAsync(
        Stream stream,
        UploadFormat format,
        Guid? accountIdOverride,
        CancellationToken cancellationToken);
}

/// <summary>
/// Parser para uploads desde la web (multipart). No reusa FileIngestionService porque
/// aquél está atado a PendingFile (filesystem) y no emite errores por fila. La lógica
/// de mapeo de filas sigue las mismas convenciones de headers/aliases.
/// </summary>
public sealed class UploadTransactionParser : IUploadTransactionParser
{
    private static readonly StringComparer HeaderComparer = StringComparer.OrdinalIgnoreCase;
    private const int Iso4217CurrencyCodeLength = 3;

    private static readonly HashSet<string> ValidTransactionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Debit", "Credit", "TransferOut", "TransferIn", "Fee", "Interest", "Adjustment"
    };

    public async Task<UploadParseResult> ParseAsync(
        Stream stream,
        UploadFormat format,
        Guid? accountIdOverride,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return format switch
        {
            UploadFormat.Csv => await ParseCsvAsync(stream, accountIdOverride, cancellationToken),
            UploadFormat.Xlsx => ParseXlsx(stream, accountIdOverride),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Formato no soportado.")
        };
    }

    private static async Task<UploadParseResult> ParseCsvAsync(
        Stream stream,
        Guid? accountIdOverride,
        CancellationToken ct)
    {
        var transactions = new List<TransactionDto>();
        var errors = new List<TransactionRowError>();

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var headerLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            errors.Add(new TransactionRowError(0, "Archivo vacío o sin cabecera."));
            return new UploadParseResult(transactions, errors);
        }

        var headers = SplitCsvLine(headerLine);
        var headerMap = BuildHeaderMap(headers);

        var missing = RequiredHeadersMissing(headerMap);
        if (missing.Length > 0)
        {
            errors.Add(new TransactionRowError(1, $"Columnas requeridas faltantes: {string.Join(", ", missing)}"));
            return new UploadParseResult(transactions, errors);
        }

        var rowNumber = 1;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            rowNumber++;

            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = SplitCsvLine(line);
            ProcessRow(headerMap, fields, rowNumber, accountIdOverride, transactions, errors);
        }

        return new UploadParseResult(transactions, errors);
    }

    private static UploadParseResult ParseXlsx(Stream stream, Guid? accountIdOverride)
    {
        var transactions = new List<TransactionDto>();
        var errors = new List<TransactionRowError>();

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null)
        {
            errors.Add(new TransactionRowError(0, "El archivo Excel no contiene hojas."));
            return new UploadParseResult(transactions, errors);
        }

        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
        {
            errors.Add(new TransactionRowError(0, "La hoja está vacía."));
            return new UploadParseResult(transactions, errors);
        }

        var rows = usedRange.RowsUsed().ToList();
        if (rows.Count < 2)
        {
            errors.Add(new TransactionRowError(0, "El archivo no tiene filas de datos."));
            return new UploadParseResult(transactions, errors);
        }

        var headerCells = rows[0].Cells().Select(c => c.GetString()).ToArray();
        var headerMap = BuildHeaderMap(headerCells);

        var missing = RequiredHeadersMissing(headerMap);
        if (missing.Length > 0)
        {
            errors.Add(new TransactionRowError(1, $"Columnas requeridas faltantes: {string.Join(", ", missing)}"));
            return new UploadParseResult(transactions, errors);
        }

        for (var i = 1; i < rows.Count; i++)
        {
            var humanRow = i + 1;
            var fields = rows[i].Cells(1, headerCells.Length)
                .Select(c => c.GetString())
                .ToArray();

            if (fields.All(string.IsNullOrWhiteSpace)) continue;

            ProcessRow(headerMap, fields, humanRow, accountIdOverride, transactions, errors);
        }

        return new UploadParseResult(transactions, errors);
    }

    private static void ProcessRow(
        IReadOnlyDictionary<string, int> headerMap,
        IReadOnlyList<string> fields,
        int rowNumber,
        Guid? accountIdOverride,
        List<TransactionDto> transactions,
        List<TransactionRowError> errors)
    {
        var row = MapRow(headerMap, fields);

        if (TryMapTransaction(row, accountIdOverride, out var transaction, out var error))
        {
            transactions.Add(transaction!);
        }
        else
        {
            errors.Add(new TransactionRowError(rowNumber, error ?? "Error desconocido"));
        }
    }

    private static bool TryMapTransaction(
        Dictionary<string, string> row,
        Guid? accountIdOverride,
        out TransactionDto? transaction,
        out string? error)
    {
        transaction = null;
        error = null;

        var externalId = ReadValue(row, "external_id", "externalid", "id");
        if (string.IsNullOrWhiteSpace(externalId))
        {
            error = "ExternalId requerido";
            return false;
        }

        Guid accountId;
        if (accountIdOverride.HasValue)
        {
            accountId = accountIdOverride.Value;
        }
        else
        {
            var accountIdValue = ReadValue(row, "account_id", "accountid");
            if (!Guid.TryParse(accountIdValue, out accountId))
            {
                error = "AccountId inválido (debe ser un GUID o usar el selector de cuenta de la UI)";
                return false;
            }
        }

        var transactionType = ReadValue(row, "transaction_type", "transactiontype", "type");
        if (string.IsNullOrWhiteSpace(transactionType))
        {
            error = "TransactionType requerido";
            return false;
        }

        if (!ValidTransactionTypes.Contains(transactionType))
        {
            error = $"TransactionType inválido: {transactionType}";
            return false;
        }

        var amountValue = ReadValue(row, "amount", "monto");
        if (string.IsNullOrWhiteSpace(amountValue) || !TryParseDecimal(amountValue, out var amount))
        {
            error = "Amount inválido";
            return false;
        }

        var currencyCode = ReadValue(row, "currency_code", "currencycode", "currency", "moneda");
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Length != Iso4217CurrencyCodeLength)
        {
            error = "CurrencyCode inválido";
            return false;
        }

        var valueDateValue = ReadValue(row, "value_date", "valuedate", "date", "fecha");
        if (string.IsNullOrWhiteSpace(valueDateValue) || !TryParseDateOnly(valueDateValue, out var valueDate))
        {
            error = "ValueDate inválido";
            return false;
        }

        DateOnly? bookingDate = null;
        var bookingDateValue = ReadValue(row, "booking_date", "bookingdate");
        if (!string.IsNullOrWhiteSpace(bookingDateValue))
        {
            if (!TryParseDateOnly(bookingDateValue, out var parsedBookingDate))
            {
                error = "BookingDate inválido";
                return false;
            }
            bookingDate = parsedBookingDate;
        }

        decimal? originalAmount = null;
        var originalAmountValue = ReadValue(row, "original_amount", "originalamount");
        if (!string.IsNullOrWhiteSpace(originalAmountValue))
        {
            if (!TryParseDecimal(originalAmountValue, out var parsed))
            {
                error = "OriginalAmount inválido";
                return false;
            }
            originalAmount = parsed;
        }

        var originalCurrency = ReadValue(row, "original_currency", "originalcurrency");
        if ((originalAmount.HasValue && string.IsNullOrWhiteSpace(originalCurrency)) ||
            (!originalAmount.HasValue && !string.IsNullOrWhiteSpace(originalCurrency)))
        {
            error = "OriginalAmount y OriginalCurrency deben venir juntos";
            return false;
        }

        transaction = new TransactionDto
        {
            ExternalId = externalId,
            AccountId = accountId,
            TransactionType = transactionType,
            Amount = amount,
            CurrencyCode = currencyCode.ToUpperInvariant(),
            ValueDate = valueDate,
            BookingDate = bookingDate,
            Description = ReadValue(row, "description", "descripcion", "detail"),
            Category = ReadValue(row, "category", "categoria"),
            CounterpartyName = ReadValue(row, "counterparty_name", "counterpartyname"),
            CounterpartyAccount = ReadValue(row, "counterparty_account", "counterpartyaccount"),
            CounterpartyBank = ReadValue(row, "counterparty_bank", "counterpartybank"),
            OriginalAmount = originalAmount,
            OriginalCurrency = originalCurrency?.ToUpperInvariant(),
        };

        return true;
    }

    // -------- Helpers --------

    private static Dictionary<string, int> BuildHeaderMap(IEnumerable<string> headers)
    {
        var map = new Dictionary<string, int>(HeaderComparer);
        var index = 0;
        foreach (var header in headers)
        {
            var normalized = NormalizeHeader(header);
            if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
            {
                map[normalized] = index;
            }
            index++;
        }
        return map;
    }

    private static string[] RequiredHeadersMissing(IReadOnlyDictionary<string, int> headerMap)
    {
        // Si el caller va a sobreescribir AccountId, esa columna no es requerida.
        // El parser no sabe eso acá → siempre pedimos todo, y si el override está activo,
        // simplemente se ignora el valor del archivo (incluso si la columna falta, el
        // TryMapTransaction lo resuelve mirando accountIdOverride primero).
        var required = new[] { "externalid", "transactiontype", "amount", "currencycode", "valuedate" };
        return required.Where(h => !headerMap.ContainsKey(h)).ToArray();
    }

    private static Dictionary<string, string> MapRow(
        IReadOnlyDictionary<string, int> headerMap,
        IReadOnlyList<string> fields)
    {
        var row = new Dictionary<string, string>(HeaderComparer);
        foreach (var (header, index) in headerMap)
        {
            row[header] = index < fields.Count ? (fields[index] ?? string.Empty).Trim() : string.Empty;
        }
        return row;
    }

    private static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return string.Empty;
        return new string(header.Trim().ToLowerInvariant()
            .Where(c => c != '_' && c != '-' && !char.IsWhiteSpace(c))
            .ToArray());
    }

    private static string? ReadValue(Dictionary<string, string> row, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var normalized = NormalizeHeader(alias);
            if (row.TryGetValue(normalized, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }
            if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result)) return true;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result)) return true;
        var normalized = value.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseDateOnly(string value, out DateOnly date)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        if (DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date)) return true;
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var excelSerial))
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(excelSerial));
            return true;
        }
        date = default;
        return false;
    }
}
