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
    /// Genera transacciones internas + reconciliaciones de demo sobre la cuenta seed.
    /// Crea 4 reconciliaciones (hoy, -7, -14, -21 días) con mix de estados:
    /// alguna aprobada, algunas con discrepancias resueltas y otras pendientes.
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
            today.AddDays(-7),
            today.AddDays(-14),
            today.AddDays(-21),
        };

        var summary = new SeedReconciliationsResponse
        {
            RunId = runId,
            ReconciliationIds = new List<Guid>()
        };

        for (var i = 0; i < dates.Length; i++)
        {
            var date = dates[i];

            // 1. Generar 8 transacciones internas balanceadas (no driftean balance).
            var txns = GenerateInternalTransactions(account, date, runId, batchIndex: i);
            foreach (var tx in txns)
            {
                account.ApplyTransaction(tx);
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

            // 4. Post-acciones para variar estados visibles en la UI:
            //    - Reconciliación 1 (más reciente): nada (todo pendiente)
            //    - Reconciliación 2: resuelve 1 discrepancia
            //    - Reconciliación 3: resuelve 2 discrepancias + aprueba
            //    - Reconciliación 4 (más antigua): aprueba directo (queda con discrepancias unresolved)
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
    /// Genera 8 transacciones balanceadas (4 créditos de 10K + 4 débitos de 5K)
    /// → impacto neto +20K COP por batch, lejos del límite del balance inicial.
    /// </summary>
    private static List<Transaction> GenerateInternalTransactions(
        Account account,
        DateOnly date,
        string runId,
        int batchIndex)
    {
        var txns = new List<Transaction>(8);
        var currency = account.Currency.Code;
        var rng = new Random(HashCode.Combine(runId, batchIndex));   // determinista por batch

        var descriptions = new[]
        {
            "Pago proveedor INSUMOS-SA",
            "Cobranza cliente ACME-LTDA",
            "Transferencia interna ahorros",
            "Pago servicios públicos",
            "Cobro factura #",
            "Comisión bancaria",
            "Depósito en efectivo sucursal",
            "Pago nómina parcial"
        };

        for (var i = 0; i < 4; i++)
        {
            // Crédito (+10.000)
            var creditDesc = $"{descriptions[i % descriptions.Length]} {rng.Next(1000, 9999)}";
            txns.Add(Transaction.CreateCredit(
                externalId: $"demo-{runId}-{date:yyyyMMdd}-c{i}",
                source: SeederTag,
                accountId: account.Id,
                amount: 10_000m,
                currencyCode: currency,
                valueDate: date,
                bookingDate: date,
                description: creditDesc));

            // Débito (−5.000)
            var debitDesc = $"{descriptions[(i + 4) % descriptions.Length]} {rng.Next(1000, 9999)}";
            txns.Add(Transaction.CreateDebit(
                externalId: $"demo-{runId}-{date:yyyyMMdd}-d{i}",
                source: SeederTag,
                accountId: account.Id,
                amount: 5_000m,
                currencyCode: currency,
                valueDate: date,
                bookingDate: date,
                description: debitDesc));
        }

        return txns;
    }

    /// <summary>
    /// Construye un statement bancario con mix de coincidencias y discrepancias:
    /// - 5 líneas que matchean exactamente
    /// - 1 con amount mismatch (+1 al monto real)
    /// - 1 con date mismatch (+1 día)
    /// - 1 línea que no existe en internas (MissingInternal en la nomenclatura del engine)
    /// - 2 transacciones internas omitidas → MissingExternal
    /// </summary>
    private static List<ExternalStatementLine> BuildStatement(
        IReadOnlyList<Transaction> internalTxns,
        string currencyCode,
        DateOnly date)
    {
        var statement = new List<ExternalStatementLine>();

        // 5 matches exactos: las primeras 5 transacciones.
        for (var i = 0; i < 5 && i < internalTxns.Count; i++)
        {
            var tx = internalTxns[i];
            statement.Add(new ExternalStatementLine(
                ExternalReference: tx.ExternalId,
                Amount: tx.Amount.Amount,
                CurrencyCode: currencyCode,
                ValueDate: tx.ValueDate,
                Description: tx.Description));
        }

        // 1 amount mismatch: tx index 5
        if (internalTxns.Count > 5)
        {
            var tx = internalTxns[5];
            statement.Add(new ExternalStatementLine(
                ExternalReference: tx.ExternalId,
                Amount: tx.Amount.Amount + (tx.Amount.Amount > 0 ? 1m : -1m),
                CurrencyCode: currencyCode,
                ValueDate: tx.ValueDate,
                Description: tx.Description));
        }

        // 1 date mismatch: tx index 6, fecha +1 día
        if (internalTxns.Count > 6)
        {
            var tx = internalTxns[6];
            statement.Add(new ExternalStatementLine(
                ExternalReference: tx.ExternalId,
                Amount: tx.Amount.Amount,
                CurrencyCode: currencyCode,
                ValueDate: tx.ValueDate.AddDays(1),
                Description: tx.Description));
        }

        // 1 missing internal: línea bancaria sin contraparte interna
        statement.Add(new ExternalStatementLine(
            ExternalReference: $"bank-only-{date:yyyyMMdd}",
            Amount: 2_500m,
            CurrencyCode: currencyCode,
            ValueDate: date,
            Description: "Cargo automático mantenimiento"));

        // tx index 7 NO aparece en statement → MissingExternal
        // (no agregamos nada, simplemente la omitimos)

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

        switch (batchIndex)
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
}
