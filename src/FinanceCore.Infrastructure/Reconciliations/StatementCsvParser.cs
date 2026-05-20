using System.Globalization;
using System.Text;
using FinanceCore.Domain.Entities;

namespace FinanceCore.Infrastructure.Reconciliations;

/// <summary>
/// Parser ligero para extractos bancarios subidos como CSV.
/// Cabeceras aceptadas (case-insensitive, alias permitidos):
///   ExternalReference (alias: reference, ref, id, externalid)
///   Amount            (alias: monto)
///   CurrencyCode      (alias: currency, moneda)
///   ValueDate         (alias: date, fecha)
///   Description       (opcional; alias: descripcion, detail)
/// </summary>
public static class StatementCsvParser
{
    private static readonly StringComparer HeaderComparer = StringComparer.OrdinalIgnoreCase;

    public sealed record ParseResult(
        IReadOnlyList<ExternalStatementLine> Lines,
        IReadOnlyList<string> Errors);

    public static async Task<ParseResult> ParseAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var lines = new List<ExternalStatementLine>();
        var errors = new List<string>();

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var headerLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            errors.Add("El archivo está vacío o no tiene cabecera.");
            return new ParseResult(lines, errors);
        }

        var headers = SplitCsvLine(headerLine);
        var headerMap = BuildHeaderMap(headers);

        if (!headerMap.ContainsKey("externalreference"))
            errors.Add("Falta columna ExternalReference.");
        if (!headerMap.ContainsKey("amount"))
            errors.Add("Falta columna Amount.");
        if (!headerMap.ContainsKey("currencycode"))
            errors.Add("Falta columna CurrencyCode.");
        if (!headerMap.ContainsKey("valuedate"))
            errors.Add("Falta columna ValueDate.");

        if (errors.Count > 0)
            return new ParseResult(lines, errors);

        var row = 1;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            row++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = SplitCsvLine(line);
            if (fields.Length == 0)
                continue;

            var reference = ReadField(headerMap, fields, "externalreference");
            var amountStr = ReadField(headerMap, fields, "amount");
            var currency = ReadField(headerMap, fields, "currencycode");
            var dateStr = ReadField(headerMap, fields, "valuedate");
            var description = ReadField(headerMap, fields, "description");

            if (string.IsNullOrWhiteSpace(reference))
            { errors.Add($"Fila {row}: ExternalReference vacía"); continue; }
            if (!TryParseDecimal(amountStr, out var amount))
            { errors.Add($"Fila {row}: Amount inválido ({amountStr})"); continue; }
            if (string.IsNullOrWhiteSpace(currency) || currency!.Length != 3)
            { errors.Add($"Fila {row}: CurrencyCode inválido"); continue; }
            if (!TryParseDate(dateStr, out var date))
            { errors.Add($"Fila {row}: ValueDate inválido ({dateStr})"); continue; }

            lines.Add(new ExternalStatementLine(
                ExternalReference: reference!.Trim(),
                Amount: amount,
                CurrencyCode: currency.Trim().ToUpperInvariant(),
                ValueDate: date,
                Description: string.IsNullOrWhiteSpace(description) ? null : description!.Trim()));
        }

        return new ParseResult(lines, errors);
    }

    private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(HeaderComparer);
        var aliases = new Dictionary<string, string[]>(HeaderComparer)
        {
            ["externalreference"] = ["externalreference", "reference", "ref", "id", "externalid"],
            ["amount"]            = ["amount", "monto"],
            ["currencycode"]      = ["currencycode", "currency", "moneda"],
            ["valuedate"]         = ["valuedate", "date", "fecha"],
            ["description"]       = ["description", "descripcion", "detail"]
        };

        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = Normalize(headers[i]);
            foreach (var (canonical, group) in aliases)
            {
                if (group.Any(a => HeaderComparer.Equals(Normalize(a), normalized)) &&
                    !map.ContainsKey(canonical))
                {
                    map[canonical] = i;
                }
            }
        }
        return map;
    }

    private static string Normalize(string s)
        => new(s.Trim().Where(c => c != '_' && c != '-' && !char.IsWhiteSpace(c)).ToArray());

    private static string? ReadField(Dictionary<string, int> map, string[] fields, string key)
    {
        if (!map.TryGetValue(key, out var idx)) return null;
        if (idx >= fields.Length) return null;
        return fields[idx];
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

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
            return true;
        var normalized = value.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
    }
}
