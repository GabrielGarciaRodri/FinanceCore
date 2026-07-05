using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.BackgroundJobs.Jobs;

namespace FinanceCore.API.Controllers;

/// <summary>
/// Endpoints exclusivos para entornos de desarrollo.
/// Cada acción verifica IWebHostEnvironment.IsDevelopment() y devuelve 404 si no
/// se está corriendo en Development. Reforzado además con [Authorize(AdminOnly)].
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = "AdminOnly")]
public class DevController : ControllerBase
{
    private static readonly Guid SeedAccountId = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000001");
    private const string SeederTag = "demo-seeder";

    private readonly ILogger<DevController> _logger;

    public DevController(ILogger<DevController> logger) => _logger = logger;

    /// <summary>
    /// Genera datos de demo con sabor de conciliación de pasarelas (Wompi/PayU/
    /// MercadoPago/ePayco/Addi) sobre la cuenta seed: liquidaciones, comisiones,
    /// retenciones, devoluciones, GMF y contracargos, repartidos en varias
    /// reconciliaciones con mix de estados (pendiente / parcialmente resuelta /
    /// aprobada) y discrepancias realistas (liquidación T+1, referencia de
    /// pasarela, contracargo bank-only, dispersión pendiente). Toda transacción
    /// queda categorizada (Liquidación pasarela, Comisiones, Impuestos y
    /// retenciones, Devoluciones, Fees bancarios, Ajustes) para que el filtro
    /// por categoría de /transactions tenga contenido real.
    /// Sólo disponible en entorno Development.
    /// </summary>
    [HttpPost("seed-reconciliations-demo")]
    [ProducesResponseType(typeof(SeedReconciliationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SeedReconciliationsDemo(
        [FromServices] IWebHostEnvironment env,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IReconciliationEngine engine,
        CancellationToken cancellationToken)
    {
        if (!env.IsDevelopment())
        {
            // Pretendemos que el endpoint no existe fuera de Development.
            return NotFound();
        }

        var account = await unitOfWork.Accounts.GetByIdAsync(SeedAccountId, cancellationToken);
        if (account is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Cuenta seed no encontrada",
                Detail = $"No existe la cuenta {SeedAccountId}. Asegurate de correr V003__Identity_Schema y el seed inicial.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Sufijo único por corrida → permite re-correr el endpoint sin colisionar
        // con externalIds previos.
        var runId = Guid.NewGuid().ToString("N")[..6];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dates = new[]
        {
            today,
            today.AddDays(-3),
            today.AddDays(-7),
            today.AddDays(-10),
            today.AddDays(-14),
            today.AddDays(-18),
            today.AddDays(-21),
            today.AddDays(-25),
        };

        var summary = new SeedReconciliationsResponse
        {
            RunId = runId,
            ReconciliationIds = new List<Guid>()
        };

        for (var i = 0; i < dates.Length; i++)
        {
            var date = dates[i];

            // 0. El engine devuelve la reconciliación existente sin procesar el
            //    extracto cuando la fecha ya tiene una en estado Completed — p.ej.
            //    la BalanceOnly vacía que deja el cierre diario de Hangfire. Si es
            //    ese artefacto vacío (0 externas, 0 matcheadas), lo removemos y
            //    re-seedeamos la fecha; si es una reconciliación con contenido,
            //    salteamos la fecha y la reportamos en la respuesta.
            var existingRec = await unitOfWork.Reconciliations
                .GetByAccountAndDateAsync(account.Id, date, cancellationToken);
            if (existingRec is not null && existingRec.Status == ReconciliationStatus.Completed)
            {
                var isEmptyBalanceArtifact =
                    existingRec.TotalExternalRecords == 0 && existingRec.MatchedCount == 0;
                if (!isEmptyBalanceArtifact)
                {
                    summary.SkippedDates.Add(date);
                    continue;
                }

                unitOfWork.Reconciliations.Remove(existingRec);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // 1. Generar liquidaciones de pasarela + costos (créditos > débitos →
            //    el balance de la cuenta corriente no queda negativo).
            var txns = GenerateInternalTransactions(account, date, runId, batchIndex: i);
            foreach (var tx in txns)
            {
                // Ciclo de vida completo hasta Posted: el engine sólo matchea
                // transacciones Posted/Reconciled. El TransactionPostedEventHandler
                // aplica el balance a la cuenta post-commit, por eso acá ya no se
                // llama a account.ApplyTransaction.
                tx.StartProcessing();
                tx.MarkAsValidated();
                tx.Post();
                unitOfWork.Transactions.Add(tx);
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
            summary.TransactionsCreated += txns.Count;

            // 2. Armar statement con mix de matching/mismatches/missing.
            var statement = BuildStatement(txns, account.Currency.Code, date);

            // 3. Reconciliar usando el engine real.
            var result = await engine.ReconcileWithStatementAsync(
                account.Id,
                date,
                statement,
                cancellationToken);

            summary.ReconciliationIds.Add(result.ReconciliationId);
            summary.DiscrepanciesCreated += result.DiscrepancyCount;

            // 4. Post-acciones (cíclicas por batch % 4) para variar estados en la UI:
            //    pendiente / resuelta parcial / resuelta + aprobada / aprobada con pendientes.
            await ApplyPostActions(unitOfWork, result.ReconciliationId, i, cancellationToken, summary);
        }

        _logger.LogInformation(
            "Demo seed completed. RunId={RunId} Txs={Txs} Recs={Recs} Discrepancies={Disc}",
            runId,
            summary.TransactionsCreated,
            summary.ReconciliationIds.Count,
            summary.DiscrepanciesCreated);

        return Ok(summary);
    }

    /// <summary>
    /// Genera las transacciones internas de un batch con sabor de pasarelas:
    /// liquidaciones (créditos, montos variados) + comisiones/retenciones/
    /// devoluciones/GMF/ajustes (débitos, como % o fracción de las liquidaciones).
    /// Cada transacción se categoriza al crearse. Devuelve créditos antes que
    /// débitos para que el balance de la cuenta corriente nunca quede negativo
    /// (los costos son una fracción de lo acreditado).
    /// </summary>
    private static List<Transaction> GenerateInternalTransactions(
        Account account,
        DateOnly date,
        string runId,
        int batchIndex)
    {
        var currency = account.Currency.Code;
        var rng = new Random(HashCode.Combine(runId, batchIndex));   // determinista por batch
        var gateways = new[] { "Wompi", "PayU", "MercadoPago", "ePayco", "Addi" };

        // Liquidaciones (créditos): montos variados 350k–4.8M, redondeados al millar.
        var credits = new List<Transaction>();
        var settlementAmounts = new List<decimal>();
        var settlementCount = 5 + (batchIndex % 2);
        for (var i = 0; i < settlementCount; i++)
        {
            var gw = gateways[rng.Next(gateways.Length)];
            var sales = rng.Next(18, 260);
            var amount = rng.Next(350, 4800) * 1000m;
            settlementAmounts.Add(amount);
            var settlement = Transaction.CreateCredit(
                externalId: $"demo-{runId}-{date:yyyyMMdd}-liq{i}",
                source: SeederTag,
                accountId: account.Id,
                amount: amount,
                currencyCode: currency,
                valueDate: date,
                bookingDate: date,
                description: $"Liquidación {gw} · {sales} ventas");
            settlement.Categorize("Liquidación pasarela", gw);
            credits.Add(settlement);
        }

        // Costos (débitos) como % de las liquidaciones → siempre < total acreditado.
        var feeKinds = new (string Label, decimal Rate, string Category)[]
        {
            ("Comisión {0} (2,99% + IVA)", 0.0299m, "Comisiones"),
            ("Retención en la fuente", 0.0150m, "Impuestos y retenciones"),
            ("Retención ICA", 0.0110m, "Impuestos y retenciones"),
            ("Comisión pasarela {0}", 0.0265m, "Comisiones"),
            ("IVA sobre comisión", 0.0057m, "Impuestos y retenciones"),
        };
        var debits = new List<Transaction>();
        var feeCount = 4 + (batchIndex % 2);
        for (var i = 0; i < feeCount; i++)
        {
            var gw = gateways[rng.Next(gateways.Length)];
            var (label, rate, category) = feeKinds[i % feeKinds.Length];
            var basis = settlementAmounts[i % settlementAmounts.Count];
            var amount = Math.Max(1000m, Math.Round(basis * rate / 1000m) * 1000m);
            var fee = Transaction.CreateDebit(
                externalId: $"demo-{runId}-{date:yyyyMMdd}-fee{i}",
                source: SeederTag,
                accountId: account.Id,
                amount: amount,
                currencyCode: currency,
                valueDate: date,
                bookingDate: date,
                description: string.Format(label, gw));
            fee.Categorize(category);
            debits.Add(fee);
        }

        // Devoluciones a clientes: montos chicos (una fracción de lo liquidado)
        // para que el total debitado siga muy por debajo de lo acreditado.
        var refundCount = 1 + (batchIndex % 2);
        for (var i = 0; i < refundCount; i++)
        {
            var gw = gateways[rng.Next(gateways.Length)];
            var order = rng.Next(40000, 99999);
            var refund = Transaction.CreateDebit(
                externalId: $"demo-{runId}-{date:yyyyMMdd}-ref{i}",
                source: SeederTag,
                accountId: account.Id,
                amount: rng.Next(45, 320) * 1000m,
                currencyCode: currency,
                valueDate: date,
                bookingDate: date,
                description: $"Devolución {gw} · pedido #{order}");
            refund.Categorize("Devoluciones", gw);
            debits.Add(refund);
        }

        // GMF 4x1000 sobre lo liquidado en el día.
        var gmf = Transaction.CreateDebit(
            externalId: $"demo-{runId}-{date:yyyyMMdd}-gmf",
            source: SeederTag,
            accountId: account.Id,
            amount: Math.Round(settlementAmounts.Sum() * 0.004m),
            currencyCode: currency,
            valueDate: date,
            bookingDate: date,
            description: "GMF 4x1000 sobre movimientos del día");
        gmf.Categorize("Fees bancarios");
        debits.Add(gmf);

        // Ajuste chico batch por medio: monto no-millar, sabor operativo.
        if (batchIndex % 2 == 1)
        {
            var adjustment = Transaction.CreateDebit(
                externalId: $"demo-{runId}-{date:yyyyMMdd}-adj",
                source: SeederTag,
                accountId: account.Id,
                amount: rng.Next(1200, 9800),
                currencyCode: currency,
                valueDate: date,
                bookingDate: date,
                description: "Ajuste por diferencia en dispersión");
            adjustment.Categorize("Ajustes");
            debits.Add(adjustment);
        }

        return credits.Concat(debits).ToList();
    }

    /// <summary>
    /// Construye el extracto del batch con discrepancias realistas de conciliación
    /// de pasarelas:
    /// - la mayoría de las liquidaciones matchean exacto,
    /// - una cae T+1 (DateMismatch, dentro de tolerancia),
    /// - otra trae la referencia de la pasarela distinta a la interna (ReferenceMismatch),
    /// - un contracargo bank-only sin contraparte interna (MissingInternal),
    /// - las 2 últimas internas no aparecen → pendiente de desglose (MissingExternal).
    /// </summary>
    private static List<ExternalStatementLine> BuildStatement(
        IReadOnlyList<Transaction> internalTxns,
        string currencyCode,
        DateOnly date)
    {
        var statement = new List<ExternalStatementLine>();
        var n = internalTxns.Count;
        var gateways = new[] { "Wompi", "PayU", "MercadoPago" };

        // Reservamos las 2 últimas internas: no van al extracto → MissingExternal.
        var matchedUpTo = Math.Max(1, n - 2);
        for (var i = 0; i < matchedUpTo; i++)
        {
            var tx = internalTxns[i];
            var reference = tx.ExternalId;
            var valueDate = tx.ValueDate;
            var description = tx.Description;

            if (i == 1)
            {
                // Liquidación abonada T+1 → DateMismatch (dentro de la tolerancia).
                valueDate = tx.ValueDate.AddDays(1);
            }
            else if (i == 2)
            {
                // El extracto trae la referencia de la pasarela, distinta a la
                // interna → ReferenceMismatch (matchea por monto y fecha iguales).
                reference = $"STL-{gateways[(date.DayNumber + i) % gateways.Length]}-{date:yyMMdd}{i}".ToUpperInvariant();
                description = $"Abono pasarela ref {reference}";
            }

            statement.Add(new ExternalStatementLine(
                ExternalReference: reference,
                Amount: tx.Amount.Amount,
                CurrencyCode: currencyCode,
                ValueDate: valueDate,
                Description: description));
        }

        // Contracargo bank-only (monto no-millar para no matchear por casualidad
        // ninguna interna) → MissingInternal.
        var gw = gateways[(date.DayNumber + n) % gateways.Length];
        var chargeback = (80 + ((date.DayNumber * 7 + n) % 90)) * 1000m + 350m;
        statement.Add(new ExternalStatementLine(
            ExternalReference: $"CHG-{date:yyMMdd}",
            Amount: -chargeback,
            CurrencyCode: currencyCode,
            ValueDate: date,
            Description: $"Contracargo {gw} · venta disputada"));

        return statement;
    }

    /// <summary>
    /// Aplica acciones de resolve/approve para variar los estados visibles.
    /// </summary>
    private static async Task ApplyPostActions(
        IUnitOfWork unitOfWork,
        Guid reconciliationId,
        int batchIndex,
        CancellationToken ct,
        SeedReconciliationsResponse summary)
    {
        var rec = await unitOfWork.Reconciliations.GetByIdAsync(reconciliationId, ct);
        if (rec is null) return;

        var discrepancies = rec.Discrepancies.ToList();

        switch (batchIndex % 4)
        {
            case 0:
                // Más reciente: sin acciones (todo pendiente).
                return;

            case 1:
                // Resuelve la primera discrepancia.
                if (discrepancies.Count > 0)
                {
                    discrepancies[0].Resolve(ResolutionType.Ignored, SeederTag,
                        "Demo: cargo de mantenimiento esperado, no requiere acción.");
                    summary.DiscrepanciesResolved++;
                }
                break;

            case 2:
                // Resuelve las primeras dos discrepancias + aprueba.
                if (discrepancies.Count > 0)
                {
                    discrepancies[0].Resolve(ResolutionType.MatchedManually, SeederTag,
                        "Demo: matcheado manualmente contra referencia externa.");
                    summary.DiscrepanciesResolved++;
                }
                if (discrepancies.Count > 1)
                {
                    discrepancies[1].Resolve(ResolutionType.AdjustmentCreated, SeederTag,
                        "Demo: ajuste contable creado.");
                    summary.DiscrepanciesResolved++;
                }
                TryApprove(rec, summary);
                break;

            case 3:
                // Más antigua: aprueba directo (queda con discrepancias unresolved).
                TryApprove(rec, summary);
                break;
        }

        unitOfWork.Reconciliations.Update(rec);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static void TryApprove(Reconciliation rec, SeedReconciliationsResponse summary)
    {
        try
        {
            rec.Approve(SeederTag, "Demo: aprobada automáticamente por el seeder.");
            summary.ReconciliationsApproved++;
        }
        catch (InvalidOperationException)
        {
            // El engine puede haber dejado la reconciliación en un estado no-terminal.
            // En ese caso la saltamos silenciosamente; igual queda en la lista.
        }
    }
}

public class SeedReconciliationsResponse
{
    public string RunId { get; set; } = null!;
    public int TransactionsCreated { get; set; }
    public int DiscrepanciesCreated { get; set; }
    public int DiscrepanciesResolved { get; set; }
    public int ReconciliationsApproved { get; set; }
    public List<Guid> ReconciliationIds { get; set; } = new();

    /// <summary>
    /// Fechas salteadas porque ya tenían una reconciliación Completed con
    /// contenido real (el seeder no pisa datos existentes).
    /// </summary>
    public List<DateOnly> SkippedDates { get; set; } = new();
}
