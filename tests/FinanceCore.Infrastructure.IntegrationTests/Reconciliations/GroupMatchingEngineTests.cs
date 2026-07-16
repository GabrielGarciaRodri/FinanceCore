using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.IntegrationTests.Fixtures;
using FinanceCore.Infrastructure.Observability;
using FinanceCore.Infrastructure.Persistence;
using FinanceCore.Infrastructure.Persistence.Repositories;
using FinanceCore.Infrastructure.Reconciliations;
using Xunit;

namespace FinanceCore.Infrastructure.IntegrationTests.Reconciliations;

/// <summary>
/// Matching N:1 (SCRUM-41): un payout de pasarela concilia contra el grupo de
/// ventas que agrupa, con la comisión generada como transacción Fee explícita.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GroupMatchingEngineTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;

    public GroupMatchingEngineTests(PostgresFixture fx)
    {
        _fx = fx;
    }

    public Task InitializeAsync() => _fx.CleanDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Payout_ReconcilesAgainstGroupedSales_WithExplicitFee()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);
        await SeedPayuProfileAsync(uow);

        // Ventas PayU de los 3 días previos (bruto 1000) + una txn bancaria del día.
        SeedPostedCredit(uow, account.Id, "sale-1", "PAYU", 300m, date.AddDays(-3));
        SeedPostedCredit(uow, account.Id, "sale-2", "PAYU", 300m, date.AddDays(-2));
        SeedPostedCredit(uow, account.Id, "sale-3", "PAYU", 400m, date.AddDays(-1));
        SeedPostedCredit(uow, account.Id, "bank-1", "BANK", 50m, date);
        await uow.SaveChangesAsync();

        // Extracto: el payout neto (1000 − 3.5% = 965) + la línea bancaria 1:1.
        var engine = BuildEngine(uow);
        var result = await engine.ReconcileWithStatementAsync(account.Id, date, new[]
        {
            new ExternalStatementLine("PAYU-LIQ-001", 965m, "USD", date, "Liquidación PAYU semana"),
            new ExternalStatementLine("bank-1", 50m, "USD", date)
        });

        // Extracto totalmente explicado => conciliación LIMPIA.
        result.Status.Should().Be(ReconciliationStatus.Completed);
        result.MatchedCount.Should().Be(4); // 3 agrupadas + 1 uno-a-uno
        result.UnmatchedInternal.Should().Be(0);
        result.UnmatchedExternal.Should().Be(0);
        result.DiscrepancyAmount.Should().Be(0m);

        // Grupo persistido con sus 3 items y la comisión explicada.
        var (__, ctx2) = BuildUow();
        await using var ___ = ctx2;

        var group = await ctx2.ReconciliationMatchGroups.Include(g => g.Items).SingleAsync();
        group.ExternalReference.Should().Be("PAYU-LIQ-001");
        group.PayoutAmount.Should().Be(965m);
        group.GroupedCount.Should().Be(3);
        group.GroupedAmount.Should().Be(1000m);
        group.FeeAmount.Should().Be(35m);
        group.Items.Should().HaveCount(3);

        // Fee explícito: transacción Fee posteada por −35.
        group.FeeTransactionId.Should().NotBeNull();
        var fee = await ctx2.Transactions.SingleAsync(t => t.Id == group.FeeTransactionId);
        fee.Type.Should().Be(TransactionType.Fee);
        fee.Status.Should().Be(TransactionStatus.Posted);
        fee.ExternalIdSource.Should().Be("SYSTEM");

        // Las ventas quedaron Reconciled apuntando a la rec del payout.
        var sales = await ctx2.Transactions
            .Where(t => t.ExternalIdSource == "PAYU")
            .ToListAsync();
        sales.Should().HaveCount(3);
        sales.Should().OnlyContain(t => t.Status == TransactionStatus.Reconciled);
        sales.Should().OnlyContain(t => t.ReconciliationId == result.ReconciliationId);
    }

    [Fact]
    public async Task StatementReRun_DoesNotDuplicateGroupsOrFees()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);
        await SeedPayuProfileAsync(uow);

        SeedPostedCredit(uow, account.Id, "sale-1", "PAYU", 600m, date.AddDays(-2));
        SeedPostedCredit(uow, account.Id, "sale-2", "PAYU", 400m, date.AddDays(-1));
        await uow.SaveChangesAsync();

        // La línea fantasma fuerza CompletedWithDiscrepancies para que el
        // re-run realmente ejecute (una rec Completed cortaría antes).
        var statement = new[]
        {
            new ExternalStatementLine("PAYU-LIQ-002", 965m, "USD", date, "Liquidación PAYU"),
            new ExternalStatementLine("ghost-1", 123m, "USD", date)
        };

        var engine = BuildEngine(uow);
        var first = await engine.ReconcileWithStatementAsync(account.Id, date, statement);
        first.Status.Should().Be(ReconciliationStatus.CompletedWithDiscrepancies);
        first.MatchedCount.Should().Be(2);

        // Re-run del mismo extracto en un scope limpio.
        var (uow2, ctx2) = BuildUow();
        await using var __ = ctx2;
        var rerunEngine = BuildEngine(uow2);
        var second = await rerunEngine.ReconcileWithStatementAsync(account.Id, date, statement);

        second.ReconciliationId.Should().Be(first.ReconciliationId);
        second.MatchedCount.Should().Be(2);
        second.DiscrepancyAmount.Should().Be(first.DiscrepancyAmount);

        var (___, ctx3) = BuildUow();
        await using var ____ = ctx3;

        (await ctx3.ReconciliationMatchGroups.CountAsync()).Should().Be(1);
        (await ctx3.Transactions.CountAsync(t => t.ExternalId.StartsWith("groupfee-"))).Should().Be(1);
    }

    [Fact]
    public async Task FeeOutsideBand_LeavesPayoutUnmatched_WithNearMissNote()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);
        await SeedPayuProfileAsync(uow);

        SeedPostedCredit(uow, account.Id, "sale-1", "PAYU", 500m, date.AddDays(-2));
        SeedPostedCredit(uow, account.Id, "sale-2", "PAYU", 500m, date.AddDays(-1));
        await uow.SaveChangesAsync();

        // Payout 900 sobre bruto 1000 → comisión implícita 10%, fuera de banda.
        var engine = BuildEngine(uow);
        var result = await engine.ReconcileWithStatementAsync(account.Id, date, new[]
        {
            new ExternalStatementLine("PAYU-LIQ-003", 900m, "USD", date, "Liquidación PAYU")
        });

        result.Status.Should().Be(ReconciliationStatus.CompletedWithDiscrepancies);
        result.MatchedCount.Should().Be(0);

        var (__, ctx2) = BuildUow();
        await using var ___ = ctx2;

        (await ctx2.ReconciliationMatchGroups.CountAsync()).Should().Be(0);

        // El near-miss queda visible en la discrepancia del payout.
        var discrepancy = await ctx2.ReconciliationDiscrepancies
            .SingleAsync(d => d.ExternalReference == "PAYU-LIQ-003");
        discrepancy.ResolutionNotes.Should().Contain("Posible payout");
        discrepancy.ResolutionNotes.Should().Contain("comisión implícita");

        // Y las ventas siguen Posted, disponibles para el próximo intento.
        (await ctx2.Transactions.CountAsync(t =>
            t.ExternalIdSource == "PAYU" && t.Status == TransactionStatus.Posted)).Should().Be(2);
    }

    [Fact]
    public async Task GroupedSale_DoesNotPollute_ItsOwnDatesReconciliation()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var saleDate = date.AddDays(-1);
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);
        await SeedPayuProfileAsync(uow);

        SeedPostedCredit(uow, account.Id, "sale-1", "PAYU", 1000m, saleDate);
        await uow.SaveChangesAsync();

        // El payout de HOY agrupa la venta de AYER.
        var engine = BuildEngine(uow);
        var payoutRun = await engine.ReconcileWithStatementAsync(account.Id, date, new[]
        {
            new ExternalStatementLine("PAYU-LIQ-004", 965m, "USD", date, "Liquidación PAYU")
        });
        payoutRun.MatchedCount.Should().Be(1);

        // Conciliar AYER (fecha de la venta) con un extracto vacío: la venta ya
        // pertenece a la rec del payout y no debe reportarse como faltante acá.
        var (uow2, ctx2) = BuildUow();
        await using var __ = ctx2;
        var saleDateEngine = BuildEngine(uow2);

        var saleDateRun = await saleDateEngine.ReconcileWithStatementAsync(
            account.Id, saleDate, Array.Empty<ExternalStatementLine>());

        saleDateRun.Status.Should().Be(ReconciliationStatus.Completed);
        saleDateRun.UnmatchedInternal.Should().Be(0);
        saleDateRun.MatchedCount.Should().Be(0);

        var (___, ctx3) = BuildUow();
        await using var ____ = ctx3;
        var saleDateRec = await ctx3.Reconciliations
            .SingleAsync(r => r.ReconciliationDate == saleDate);
        saleDateRec.TotalInternalRecords.Should().Be(0);
    }

    // ----------------------------------------------------------------- helpers

    private (IUnitOfWork uow, Persistence.Context.FinanceCoreDbContext ctx) BuildUow()
    {
        var ctx = _fx.CreateDbContext();
        var cache = _fx.CreateMemoryCache();

        var txRepo = new TransactionRepository(ctx, NullLogger<TransactionRepository>.Instance);
        var acctRepo = new AccountRepository(ctx);
        var dailyRepo = new DailyBalanceRepository(ctx);
        var fxRepo = new ExchangeRateRepository(ctx, cache);
        var recRepo = new ReconciliationRepository(ctx);
        var profileRepo = new ReconciliationSourceProfileRepository(ctx);
        var alertRepo = new AlertRuleRepository(ctx);

        var uow = new UnitOfWork(ctx, txRepo, acctRepo, dailyRepo, fxRepo, recRepo, profileRepo, alertRepo);
        return (uow, ctx);
    }

    private static ReconciliationEngine BuildEngine(IUnitOfWork uow) =>
        new(
            NullLogger<ReconciliationEngine>.Instance,
            uow,
            Options.Create(new ReconciliationOptions()),
            new ReconciliationMetrics());

    private static async Task SeedPayuProfileAsync(IUnitOfWork uow)
    {
        var profile = ReconciliationSourceProfile.Create(
            accountId: null,
            sourceKey: "PAYU",
            displayName: "PayU",
            payoutPattern: "PAYU",
            internalMatchField: InternalMatchField.ExternalIdSource,
            internalMatchPattern: "^PAYU$",
            expectedFeePercent: 0.035m,
            feeTolerancePercent: 0.005m,
            groupingWindowDays: 7);

        uow.SourceProfiles.Add(profile);
        await uow.SaveChangesAsync();
    }

    private static void SeedPostedCredit(
        IUnitOfWork uow,
        Guid accountId,
        string externalId,
        string source,
        decimal amount,
        DateOnly date)
    {
        var tx = Transaction.CreateCredit(
            externalId, source, accountId, amount, "USD", date, date, $"Seed {externalId}");
        tx.StartProcessing();
        tx.MarkAsValidated();
        tx.Post();
        uow.Transactions.Add(tx);
    }

    private async Task<Account> SeedCheckingAccountAsync(IUnitOfWork uow)
    {
        // Insertar institución mínima vía SQL para no depender de Institution factory
        await using var conn = new Npgsql.NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        var institutionId = Guid.NewGuid();
        await using (var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO institutions (id, code, name, country_code, is_active) " +
            "VALUES (@id, @code, @name, 'CO', true)",
            conn))
        {
            cmd.Parameters.AddWithValue("id", institutionId);
            cmd.Parameters.AddWithValue("code", $"INST-{Guid.NewGuid().ToString("N")[..6]}");
            cmd.Parameters.AddWithValue("name", "Test Institution");
            await cmd.ExecuteNonQueryAsync();
        }

        var account = Account.CreateCheckingAccount(
            accountNumber: $"ACC-{Guid.NewGuid().ToString("N")[..10]}",
            accountName: "Test Account",
            currencyCode: "USD",
            institutionId: institutionId,
            initialBalance: 1_000_000m);

        uow.Accounts.Add(account);
        await uow.SaveChangesAsync();
        return account;
    }
}
