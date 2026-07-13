using System.Globalization;

namespace FinanceCore.Domain.Services;

/// <summary>
/// Candidata para un grupo de matching N:1 (proyección mínima de una transacción).
/// </summary>
public record GroupCandidate(Guid TransactionId, decimal Amount, DateOnly ValueDate);

/// <summary>
/// Grupo propuesto por el matcher: el conjunto de transacciones cuya suma
/// explica el payout dentro de la banda de comisión esperada.
/// </summary>
public record GroupMatchProposal(
    IReadOnlyList<GroupCandidate> Items,
    decimal GroupedAmount,
    decimal FeeAmount,
    decimal FeePercent,
    DateOnly WindowStart,
    DateOnly WindowEnd);

/// <summary>
/// Resultado del matcher: un match, un near-miss (con nota diagnóstica) o nada.
/// </summary>
public record GroupMatchResult
{
    public GroupMatchProposal? Match { get; init; }
    public string? NearMissNote { get; init; }

    public static readonly GroupMatchResult None = new();
    public static GroupMatchResult Matched(GroupMatchProposal proposal) => new() { Match = proposal };
    public static GroupMatchResult NearMiss(string note) => new() { NearMissNote = note };
}

/// <summary>
/// Selección del grupo de ventas que explica un payout de pasarela
/// (matching N:1, SCRUM-41). Servicio de dominio puro, sin persistencia.
///
/// Estrategia (sin subset-sum — ver docs/design/SCRUM-41-group-matching.md):
/// los payouts reales cubren períodos de corte CONTIGUOS, así que se evalúan
/// todos los sub-rangos de fechas contiguos del pool con sumas por prefijo —
/// O(d²) con d ≤ ventana de agrupación. La ventana completa es uno de esos
/// rangos y, por la preferencia de selección, gana cuando está en banda.
/// </summary>
public static class GroupMatcher
{
    /// <summary>
    /// Busca el grupo de candidatas cuya suma bruta S explica el payout P con
    /// una comisión implícita (S − P) / S dentro de la banda esperada ± tolerancia.
    /// </summary>
    /// <param name="payoutAmount">Monto neto depositado por la pasarela (P &gt; 0).</param>
    /// <param name="payoutDate">Fecha del payout (fin natural de la ventana).</param>
    /// <param name="pool">Candidatas ya filtradas por fuente/estado/ventana. Puede incluir devoluciones (montos negativos).</param>
    /// <param name="expectedFeePercent">Comisión esperada como fracción (0.035 = 3.5%).</param>
    /// <param name="feeTolerancePercent">Semibanda de tolerancia (0.005 = ±0.5%).</param>
    public static GroupMatchResult FindGroup(
        decimal payoutAmount,
        DateOnly payoutDate,
        IReadOnlyList<GroupCandidate> pool,
        decimal expectedFeePercent,
        decimal feeTolerancePercent)
    {
        if (payoutAmount <= 0 || pool.Count == 0)
            return GroupMatchResult.None;

        var feeMin = Math.Max(0m, expectedFeePercent - feeTolerancePercent);
        var feeMax = expectedFeePercent + feeTolerancePercent;

        // Candidatas agrupadas por fecha, ordenadas ascendente: la unidad del
        // algoritmo es el día, no la transacción individual.
        var byDate = pool
            .GroupBy(c => c.ValueDate)
            .OrderBy(g => g.Key)
            .Select(g => (Date: g.Key, Sum: g.Sum(c => c.Amount), Items: g.ToList()))
            .ToList();

        var d = byDate.Count;

        // Sumas por prefijo: prefix[i] = suma de los días [0, i).
        var prefix = new decimal[d + 1];
        for (var i = 0; i < d; i++)
            prefix[i + 1] = prefix[i] + byDate[i].Sum;

        // Mejor rango EN banda: preferir el que termina más cerca del payout
        // (end mayor); a igual end, el de mayor suma (start menor con S válido).
        (int Start, int End, decimal Sum)? best = null;
        // Mejor near-miss (S >= P): comisión implícita más cercana a la esperada.
        (decimal Sum, decimal ImpliedFee)? nearest = null;

        for (var end = d - 1; end >= 0; end--)
        {
            for (var start = 0; start <= end; start++)
            {
                var sum = prefix[end + 1] - prefix[start];
                if (sum < payoutAmount || sum <= 0)
                    continue;

                var impliedFee = (sum - payoutAmount) / sum;

                if (impliedFee >= feeMin && impliedFee <= feeMax)
                {
                    if (best == null || end > best.Value.End ||
                        (end == best.Value.End && sum > best.Value.Sum))
                    {
                        best = (start, end, sum);
                    }
                }
                else if (nearest == null ||
                         Math.Abs(impliedFee - expectedFeePercent) <
                         Math.Abs(nearest.Value.ImpliedFee - expectedFeePercent))
                {
                    nearest = (sum, impliedFee);
                }
            }

            // El mejor posible ya no puede mejorar: ends menores pierden siempre.
            if (best != null)
                break;
        }

        if (best != null)
        {
            var (start, end, sum) = best.Value;
            var items = byDate
                .Skip(start)
                .Take(end - start + 1)
                .SelectMany(day => day.Items)
                .ToList();

            return GroupMatchResult.Matched(new GroupMatchProposal(
                Items: items,
                GroupedAmount: sum,
                FeeAmount: sum - payoutAmount,
                FeePercent: sum != 0 ? (sum - payoutAmount) / sum : 0m,
                WindowStart: byDate[start].Date,
                WindowEnd: byDate[end].Date));
        }

        if (nearest != null)
        {
            var (sum, impliedFee) = nearest.Value;
            var note = string.Format(
                CultureInfo.InvariantCulture,
                "Posible payout: el mejor grupo contiguo suma {0:0.####} " +
                "(comisión implícita {1:P2}, banda aceptada [{2:P2}, {3:P2}]). " +
                "Revisar la tolerancia de comisión o la ventana del perfil.",
                sum, impliedFee, feeMin, feeMax);
            return GroupMatchResult.NearMiss(note);
        }

        return GroupMatchResult.None;
    }
}
