using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using FinanceCore.Domain.Enums;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.IntegrationTests.Fixtures;

/// <summary>
/// Levanta un contenedor PostgreSQL para la collection completa y aplica las
/// migraciones SQL (V001 + V002) una sola vez. Cada test recibe un DbContext
/// recién creado pero comparte la misma DB; los tests deben limpiar la data
/// que insertan en el TearDown si interfieren entre sí.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("financecore_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await ApplyMigrationsAsync();

        var builder = new NpgsqlDataSourceBuilder(ConnectionString);
        builder.MapEnum<TransactionType>("transaction_type", new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator());
        builder.MapEnum<TransactionStatus>("transaction_status", new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator());
        builder.MapEnum<AccountType>("account_type", new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator());
        builder.MapEnum<ReconciliationStatus>("reconciliation_status", new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator());
        builder.MapEnum<SourceType>("source_type", new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator());
        DataSource = builder.Build();
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null)
            await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    public FinanceCoreDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinanceCoreDbContext>()
            .UseNpgsql(DataSource)
            .EnableSensitiveDataLogging()
            .Options;

        return new FinanceCoreDbContext(options);
    }

    public IMemoryCache CreateMemoryCache() =>
        new MemoryCache(new MemoryCacheOptions());

    private async Task ApplyMigrationsAsync()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var migrationsDir = Path.Combine(assemblyDir, "Migrations");

        if (!Directory.Exists(migrationsDir))
            throw new InvalidOperationException(
                $"Migrations directory not found at {migrationsDir}. " +
                "Verify csproj copies database/migrations/*.sql into the output.");

        var scripts = Directory.GetFiles(migrationsDir, "V*.sql")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        foreach (var script in scripts)
        {
            var sql = await File.ReadAllTextAsync(script);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task CleanDataAsync()
    {
        // Trunca las tablas con TRUNCATE ... CASCADE para resetear entre tests.
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        const string sql = @"
            TRUNCATE TABLE
                reconciliation_match_group_items,
                reconciliation_match_groups,
                reconciliation_source_profiles,
                reconciliation_discrepancies,
                reconciliations,
                transaction_sources,
                financial_entries,
                transactions,
                daily_balances,
                financial_accounts
            RESTART IDENTITY CASCADE;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres collection";
}
