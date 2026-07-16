using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Events;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Alerting;
using FinanceCore.Infrastructure.IntegrationTests.Fixtures;
using FinanceCore.Infrastructure.Persistence;
using FinanceCore.Infrastructure.Persistence.Repositories;
using Xunit;

namespace FinanceCore.Infrastructure.IntegrationTests.Alerting;

[Collection(PostgresCollection.Name)]
public class BusinessAlertRuleTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;

    public BusinessAlertRuleTests(PostgresFixture fx)
    {
        _fx = fx;
    }

    public Task InitializeAsync() => _fx.CleanDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Dispatcher de prueba: captura en vez de enviar.</summary>
    private sealed class CapturingDispatcher : IAlertDispatcher
    {
        public List<Alert> Dispatched { get; } = new();

        public Task DispatchAsync(Alert alert, CancellationToken cancellationToken = default)
        {
            Dispatched.Add(alert);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AlertRule_RoundTrips_ThroughPostgres()
    {
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);
        var profile = ReconciliationSourceProfile.Create(
            null, "PAYU", "PayU Colombia", "^PAYU-LIQ",
            InternalMatchField.ExternalIdSource, "^PAYU-VENTAS", 0.035m, 0.005m, 7);
        uow.SourceProfiles.Add(profile);

        var rule = AlertRule.Create(
            "Payout PayU no llegó", AlertRuleType.MissingPayout,
            account.Id, profile.Id, null, null, lookbackDays: 7,
            AlertChannels.Email | AlertChannels.Webhook, "ops@acme.co", cooldownHours: 48);
        uow.AlertRules.Add(rule);
        await uow.SaveChangesAsync();

        // Releer con un contexto limpio: mappings + flags-as-string intactos.
        var (uow2, ctx2) = BuildUow();
        await using var __ = ctx2;

        var reloaded = (await uow2.AlertRules.GetEnabledByTypeAsync(AlertRuleType.MissingPayout))
            .Should().ContainSingle().Subject;

        reloaded.Name.Should().Be("Payout PayU no llegó");
        reloaded.SourceProfileId.Should().Be(profile.Id);
        reloaded.LookbackDays.Should().Be(7);
        reloaded.Channels.Should().Be(AlertChannels.Email | AlertChannels.Webhook);
        reloaded.EmailTo.Should().Be("ops@acme.co");
        reloaded.CooldownHours.Should().Be(48);
        reloaded.LastTriggeredAt.Should().BeNull();
    }

    [Fact]
    public async Task DiscrepancyRule_Dispatches_AndCooldownSuppressesRepeat()
    {
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);

        var rule = AlertRule.Create(
            "Descuadre grande", AlertRuleType.DiscrepancyThreshold,
            accountId: null, null, thresholdAmount: 100_000m, null, null,
            AlertChannels.Email, emailTo: null);
        uow.AlertRules.Add(rule);
        await uow.SaveChangesAsync();

        var dispatcher = new CapturingDispatcher();
        var handler = new BusinessRuleAlertHandler(
            uow, dispatcher, NullLogger<BusinessRuleAlertHandler>.Instance);

        var evt = new ReconciliationCompletedEvent(
            Guid.NewGuid(), account.Id, DateOnly.FromDateTime(DateTime.UtcNow),
            matchedCount: 5, unmatchedCount: 2, discrepancyAmount: -150_000m);

        await handler.Handle(evt, CancellationToken.None);

        var alert = dispatcher.Dispatched.Should().ContainSingle().Subject;
        alert.Type.Should().Be("business.discrepancythreshold");
        alert.Severity.Should().Be(AlertSeverity.Error);
        alert.Channels.Should().Contain("email").And.Contain("logging");
        alert.Properties["ruleId"].Should().Be(rule.Id);

        // El handler no guarda (lo hace el save recursivo del DbContext en
        // producción); acá simulamos ese flush y verificamos persistencia.
        await uow.SaveChangesAsync();

        var (uow2, ctx2) = BuildUow();
        await using var __ = ctx2;
        var reloaded = await uow2.AlertRules.GetByIdAsync(rule.Id);
        reloaded!.LastTriggeredAt.Should().NotBeNull();

        // Segundo evento inmediato: el cooldown lo suprime.
        var handler2 = new BusinessRuleAlertHandler(
            uow2, dispatcher, NullLogger<BusinessRuleAlertHandler>.Instance);
        await handler2.Handle(evt, CancellationToken.None);

        dispatcher.Dispatched.Should().HaveCount(1);
    }

    [Fact]
    public async Task DiscrepancyRule_BelowThreshold_DoesNotDispatch()
    {
        var (uow, ctx) = BuildUow();
        await using var _ = ctx;

        var account = await SeedCheckingAccountAsync(uow);

        var rule = AlertRule.Create(
            "Descuadre grande", AlertRuleType.DiscrepancyThreshold,
            null, null, thresholdAmount: 100_000m, null, null,
            AlertChannels.Email, null);
        uow.AlertRules.Add(rule);
        await uow.SaveChangesAsync();

        var dispatcher = new CapturingDispatcher();
        var handler = new BusinessRuleAlertHandler(
            uow, dispatcher, NullLogger<BusinessRuleAlertHandler>.Instance);

        await handler.Handle(
            new ReconciliationCompletedEvent(
                Guid.NewGuid(), account.Id, DateOnly.FromDateTime(DateTime.UtcNow),
                5, 1, discrepancyAmount: -50_000m),
            CancellationToken.None);

        dispatcher.Dispatched.Should().BeEmpty();

        var reloaded = await uow.AlertRules.GetByIdAsync(rule.Id);
        reloaded!.LastTriggeredAt.Should().BeNull();
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

    private async Task<Account> SeedCheckingAccountAsync(IUnitOfWork uow)
    {
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
