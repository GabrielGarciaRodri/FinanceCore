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

[Collection(PostgresCollection.Name)]
public class ReconciliationEngineTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;

    public ReconciliationEngineTests(PostgresFixture fx)
    {
        _fx = fx;
    }

    public Task InitializeAsync() => _fx.CleanDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BalanceOnlyPass_DoesNotOverwrite_CompletedWithDiscrepancies()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Cuenta + dos transacciones Posted del día.
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);
        SeedPostedCredit(uow, account.Id, "ext-1", 100m, date);
        SeedPostedCredit(uow, account.Id, "ext-2", 250m, date);
        await uow.SaveChangesAsync();

        // 2. Conciliación con extracto: 1 matchea, 1 línea fantasma y ext-2 sin
        //    contraparte => CompletedWithDiscrepancies con números de matching.
        var engine = BuildEngine(uow);
        var statementResult = await engine.ReconcileWithStatementAsync(
            account.Id,
            date,
            new[]
            {
                new ExternalStatementLine("ext-1", 100m, "USD", date),
                new ExternalStatementLine("ext-ghost", 999m, "USD", date)
            });

        statementResult.Status.Should().Be(ReconciliationStatus.CompletedWithDiscrepancies);
        var snapshot = statementResult;

        // 3. BalanceOnly sobre la misma cuenta/fecha (uow limpio,
        //    como lo haría el DailyCloseJob en otro scope).
        var (uow2, ctx2) = BuildUow();
        await using var __ = ctx2;
        var nightlyEngine = BuildEngine(uow2);

        var balanceResult = await nightlyEngine.ReconcileAsync(account.Id, date);

        // La pasada devuelve la rec existente sin tocarla.
        balanceResult.ReconciliationId.Should().Be(snapshot.ReconciliationId);
        balanceResult.Status.Should().Be(ReconciliationStatus.CompletedWithDiscrepancies);
        balanceResult.MatchedCount.Should().Be(snapshot.MatchedCount);

        // 4. Releer de la base: los números del matching de extracto sobreviven.
        var (___, ctx3) = BuildUow();
        await using var ____ = ctx3;

        (await ctx3.Reconciliations.CountAsync()).Should().Be(1);
        var persisted = await ctx3.Reconciliations.SingleAsync();
        persisted.Status.Should().Be(ReconciliationStatus.CompletedWithDiscrepancies);
        persisted.MatchedCount.Should().Be(snapshot.MatchedCount);
        persisted.UnmatchedInternal.Should().Be(snapshot.UnmatchedInternal);
        persisted.UnmatchedExternal.Should().Be(snapshot.UnmatchedExternal);
        persisted.TotalInternalRecords.Should().Be(2);
        persisted.TotalExternalRecords.Should().Be(2);
        persisted.DiscrepancyAmount.Should().Be(snapshot.DiscrepancyAmount);
    }

    [Fact]
    public async Task BalanceOnlyPass_StillRuns_WhenNoReconciliationExists()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);
        SeedPostedCredit(uow, account.Id, "ext-solo", 75m, date);
        await uow.SaveChangesAsync();

        var engine = BuildEngine(uow);
        var result = await engine.ReconcileAsync(account.Id, date);

        // Sin rec previa la pasada de balance crea la suya normalmente.
        result.ReconciliationId.Should().NotBeEmpty();
        (await ctx.Reconciliations.CountAsync()).Should().Be(1);
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

        var uow = new UnitOfWork(ctx, txRepo, acctRepo, dailyRepo, fxRepo, recRepo, profileRepo);
        return (uow, ctx);
    }

    private static ReconciliationEngine BuildEngine(IUnitOfWork uow) =>
        new(
            NullLogger<ReconciliationEngine>.Instance,
            uow,
            Options.Create(new ReconciliationOptions()),
            new ReconciliationMetrics());

    private static void SeedPostedCredit(
        IUnitOfWork uow,
        Guid accountId,
        string externalId,
        decimal amount,
        DateOnly date)
    {
        var tx = Transaction.CreateCredit(
            externalId, "TEST", accountId, amount, "USD", date, date, $"Seed {externalId}");
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
