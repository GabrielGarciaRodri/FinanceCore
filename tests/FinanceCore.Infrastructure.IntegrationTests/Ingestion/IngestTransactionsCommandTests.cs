using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FinanceCore.Application.Common.Models;
using FinanceCore.Application.Transactions.Commands.IngestTransactions;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.IntegrationTests.Fixtures;
using FinanceCore.Infrastructure.Persistence;
using FinanceCore.Infrastructure.Persistence.Repositories;
using Xunit;

namespace FinanceCore.Infrastructure.IntegrationTests.Ingestion;

[Collection(PostgresCollection.Name)]
public class IngestTransactionsCommandTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;

    public IngestTransactionsCommandTests(PostgresFixture fx)
    {
        _fx = fx;
    }

    public Task InitializeAsync() => _fx.CleanDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task IngestBatch_HappyPath_PersistsAllTransactions()
    {
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);

        var handler = new IngestTransactionsCommandHandler(uow, NullLogger<IngestTransactionsCommandHandler>.Instance);
        var command = new IngestTransactionsCommand
        {
            Source = "TEST",
            SourceType = SourceType.Manual,
            Transactions = new[]
            {
                Dto("ext-1", account.Id, "credit", 100m),
                Dto("ext-2", account.Id, "credit", 250m),
                Dto("ext-3", account.Id, "debit", 50m)
            }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Succeeded.Should().Be(3);
        result.Value!.Duplicates.Should().Be(0);
        result.Value!.Failed.Should().Be(0);

        (await ctx.Transactions.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task IngestBatch_SameExternalIdTwice_TreatsSecondAsDuplicate()
    {
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);
        var handler = new IngestTransactionsCommandHandler(uow, NullLogger<IngestTransactionsCommandHandler>.Instance);

        // Primer batch
        await handler.Handle(new IngestTransactionsCommand
        {
            Source = "TEST",
            SourceType = SourceType.Manual,
            Transactions = new[] { Dto("ext-dup", account.Id, "credit", 100m) }
        }, CancellationToken.None);

        // Segundo batch con el MISMO ExternalId + mismo Source
        var (uow2, ctx2) = BuildUow();
        await using var __ = ctx2;
        var handler2 = new IngestTransactionsCommandHandler(uow2, NullLogger<IngestTransactionsCommandHandler>.Instance);

        var result = await handler2.Handle(new IngestTransactionsCommand
        {
            Source = "TEST",
            SourceType = SourceType.Manual,
            Transactions = new[] { Dto("ext-dup", account.Id, "credit", 100m) }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Duplicates.Should().Be(1);
        result.Value!.Succeeded.Should().Be(0);

        (await ctx2.Transactions.CountAsync(t => t.ExternalId == "ext-dup")).Should().Be(1);
    }

    [Fact]
    public async Task IngestBatch_UnknownAccount_FailsRowButBatchContinues()
    {
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);
        var unknownAccount = Guid.NewGuid();
        var handler = new IngestTransactionsCommandHandler(uow, NullLogger<IngestTransactionsCommandHandler>.Instance);

        var result = await handler.Handle(new IngestTransactionsCommand
        {
            Source = "TEST",
            SourceType = SourceType.Manual,
            Transactions = new[]
            {
                Dto("ext-ok", account.Id, "credit", 50m),
                Dto("ext-bad", unknownAccount, "credit", 60m),
                Dto("ext-ok-2", account.Id, "debit", 20m)
            }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Succeeded.Should().Be(2);
        result.Value!.Failed.Should().Be(1);

        var saved = await ctx.Transactions.ToListAsync();
        saved.Should().HaveCount(2);
        saved.Select(t => t.ExternalId).Should().BeEquivalentTo(new[] { "ext-ok", "ext-ok-2" });
    }

    [Fact]
    public async Task IngestBatch_FailOnFirstError_RollsBackEverything()
    {
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);
        var unknownAccount = Guid.NewGuid();
        var handler = new IngestTransactionsCommandHandler(uow, NullLogger<IngestTransactionsCommandHandler>.Instance);

        var result = await handler.Handle(new IngestTransactionsCommand
        {
            Source = "TEST",
            SourceType = SourceType.Manual,
            FailOnFirstError = true,
            Transactions = new[]
            {
                Dto("ext-ok", account.Id, "credit", 50m),
                Dto("ext-bad", unknownAccount, "credit", 60m),
                Dto("ext-never", account.Id, "credit", 70m)
            }
        }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();

        // Como falló, NINGUNA transacción debería haberse persistido.
        var (uow2, ctx2) = BuildUow();
        await using var __ = ctx2;
        (await ctx2.Transactions.CountAsync()).Should().Be(0);
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

        var uow = new UnitOfWork(ctx, txRepo, acctRepo, dailyRepo, fxRepo, recRepo);
        return (uow, ctx);
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

    private static TransactionDto Dto(string externalId, Guid accountId, string type, decimal amount) => new()
    {
        ExternalId = externalId,
        AccountId = accountId,
        TransactionType = type,
        Amount = amount,
        CurrencyCode = "USD",
        ValueDate = DateOnly.FromDateTime(DateTime.UtcNow)
    };
}
