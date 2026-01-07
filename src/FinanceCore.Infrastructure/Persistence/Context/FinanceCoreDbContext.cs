using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;

namespace FinanceCore.Infrastructure.Persistence.Context;

/// <summary>
/// DbContext principal de FinanceCore.
/// </summary>
public class FinanceCoreDbContext : DbContext
{
    public FinanceCoreDbContext(DbContextOptions<FinanceCoreDbContext> options) 
        : base(options)
    {
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<DailyBalance> DailyBalances => Set<DailyBalance>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<FinancialEntry> FinancialEntries => Set<FinancialEntry>();
    public DbSet<TransactionSource> TransactionSources => Set<TransactionSource>();
    public DbSet<Reconciliation> Reconciliations => Set<Reconciliation>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceCoreDbContext).Assembly);
        modelBuilder.HasPostgresExtension("uuid-ossp");

        // Convención para nombres en snake_case
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));

            foreach (var key in entity.GetKeys())
                key.SetName(ToSnakeCase(key.GetName()!));

            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));

            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
        }
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0) result.Append('_');
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
}

#region Entity Configurations

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => new { t.ExternalId, t.ExternalIdSource })
            .IsUnique()
            .HasDatabaseName("ix_transactions_external_id");

        builder.HasIndex(t => t.Hash)
            .HasDatabaseName("ix_transactions_hash");

        builder.HasIndex(t => new { t.AccountId, t.ValueDate })
            .HasDatabaseName("ix_transactions_account_date");

        builder.Property(t => t.ExternalId).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ExternalIdSource).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Type).HasConversion<int>().IsRequired();
        builder.Property(t => t.Status).HasConversion<int>().IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.Category).HasMaxLength(50);
        builder.Property(t => t.Hash).HasMaxLength(64).IsRequired();

        builder.Ignore(t => t.DomainEvents);
    }
}

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.AccountNumber, a.InstitutionId })
            .IsUnique()
            .HasDatabaseName("ix_accounts_number_institution");

        builder.Property(a => a.AccountNumber).HasMaxLength(34).IsRequired();
        builder.Property(a => a.AccountName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Type).HasConversion<int>().IsRequired();
        builder.Property(a => a.Version).IsConcurrencyToken().HasDefaultValue(1);

        builder.Ignore(a => a.DomainEvents);
    }
}

public class DailyBalanceConfiguration : IEntityTypeConfiguration<DailyBalance>
{
    public void Configure(EntityTypeBuilder<DailyBalance> builder)
    {
        builder.HasKey(d => d.Id);

        builder.HasIndex(d => new { d.AccountId, d.BalanceDate })
            .IsUnique()
            .HasDatabaseName("ix_daily_balances_account_date");

        builder.Property(d => d.OpeningBalance).HasPrecision(18, 4).IsRequired();
        builder.Property(d => d.ClosingBalance).HasPrecision(18, 4).IsRequired();
        builder.Property(d => d.TotalDebits).HasPrecision(18, 4).HasDefaultValue(0);
        builder.Property(d => d.TotalCredits).HasPrecision(18, 4).HasDefaultValue(0);
    }
}

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.FromCurrency, e.ToCurrency, e.EffectiveDate })
            .HasDatabaseName("ix_exchange_rates_lookup");

        builder.Property(e => e.FromCurrency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.ToCurrency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Rate).HasPrecision(18, 8).IsRequired();
        builder.Property(e => e.InverseRate).HasPrecision(18, 8).IsRequired();
        builder.Property(e => e.Source).HasMaxLength(50).IsRequired();
    }
}

#endregion

#region AuditLog Entity

public class AuditLog
{
    public long Id { get; set; }
    public string EntityType { get; set; } = null!;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = null!;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public Guid? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

#endregion
