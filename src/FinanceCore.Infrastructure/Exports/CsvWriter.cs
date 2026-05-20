using System.Globalization;
using System.Text;

namespace FinanceCore.Infrastructure.Exports;

/// <summary>
/// Helpers de bajo nivel para escribir CSV RFC 4180-compatible.
/// Pensado para streaming: el caller escribe línea a línea sin materializar todo en memoria.
/// </summary>
public static class CsvWriter
{
    public static string FormatRow(params object?[] fields)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(EscapeField(fields[i]));
        }
        return sb.ToString();
    }

    public static string EscapeField(object? value)
    {
        if (value is null) return string.Empty;

        var s = value switch
        {
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            double db => db.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        var needsQuoting = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (!needsQuoting) return s;

        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
