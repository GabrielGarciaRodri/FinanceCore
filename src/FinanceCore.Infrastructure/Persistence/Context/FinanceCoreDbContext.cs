using Microsoft.EntityFrameworkCore;
using static FinanceCore.Domain.Entities.Transaction;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.ValueObjects;
using FinanceCore.Infrastructure.Services;

namespace FinanceCore.Infrastructure.Persistence.Context;

public class FinanceCoreDbContext : DbContext
{
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    public FinanceCoreDbContext(DbContextOptions<FinanceCoreDbContext> options) : base(options) { }

    public FinanceCoreDbContext(
        DbContextOptions<FinanceCoreDbContext> options,
        IDomainEventDispatcher domainEventDispatcher) : base(options)
    {
        _domainEventDispatcher = domainEventDispatcher;
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<DailyBalance> DailyBalances => Set<DailyBalance>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Institution> Institutions => Set<Institution>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Dispatch domain events before saving
        if (_domainEventDispatcher != null)
        {
            var entitiesWithEvents = ChangeTracker.Entries<BaseEntity>()
                .Where(e => e.Entity.DomainEvents.Any())
                .Select(e => e.Entity)
                .ToList();

            if (entitiesWithEvents.Count > 0)
            {
                await _domainEventDispatcher.DispatchEventsAsync(entitiesWithEvents, cancellationToken);
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Register PostgreSQL enums
        modelBuilder.HasPostgresEnum<TransactionType>("transaction_type");
        modelBuilder.HasPostgresEnum<TransactionStatus>("transaction_status");
        modelBuilder.HasPostgresEnum<AccountType>("account_type");
        modelBuilder.HasPostgresEnum<ReconciliationStatus>("reconciliation_status");
        modelBuilder.HasPostgresEnum<SourceType>("source_type");

        // Excluir Value Objects del modelo
        modelBuilder.Ignore<Money>();
        modelBuilder.Ignore<Currency>();
        modelBuilder.Ignore<CounterpartyInfo>();
        modelBuilder.Ignore<CounterpartyInfo>();

        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new DailyBalanceConfiguration());
        modelBuilder.ApplyConfiguration(new ExchangeRateConfiguration());
        modelBuilder.ApplyConfiguration(new InstitutionConfiguration());

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName != null)
                entity.SetTableName(ToSnakeCase(tableName));

            foreach (var property in entity.GetProperties())
            {
                var columnName = property.GetColumnName();
                if (columnName != null)
                    property.SetColumnName(ToSnakeCase(columnName));
            }

            foreach (var key in entity.GetKeys())
            {
                var keyName = key.GetName();
                if (keyName != null)
                    key.SetName(ToSnakeCase(keyName));
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                var fkName = fk.GetConstraintName();
                if (fkName != null)
                    fk.SetConstraintName(ToSnakeCase(fkName));
            }
        }
    }

    private static string ToSnakeCase(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToLower();
}

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => new { t.ExternalId, t.ExternalIdSource })
            .IsUnique()
            .HasDatabaseName("ix_transactions_external_id");

        builder.HasIndex(t => t.Hash).HasDatabaseName("ix_transactions_hash");
        builder.HasIndex(t => new { t.AccountId, t.ValueDate }).HasDatabaseName("ix_transactions_account_date");

        builder.Property(t => t.ExternalId).HasMaxLength(100);
        builder.Property(t => t.ExternalIdSource).HasMaxLength(50);
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.Category).HasMaxLength(50);
        builder.Property(t => t.SubCategory).HasColumnName("subcategory").HasMaxLength(50);
        builder.Property(t => t.Hash).HasMaxLength(64);
        builder.Property(t => t.Type).HasColumnName("transaction_type").IsRequired();
        builder.Property(t => t.Status).IsRequired();

        // Ignorar complex/navigation properties not mapped to columns
        builder.Ignore(t => t.Amount);
        builder.Ignore(t => t.OriginalAmount);
        builder.Ignore(t => t.Counterparty);
        builder.Ignore(t => t.Metadata);
        builder.Ignore(t => t.Tags);
        builder.Ignore(t => t.DomainEvents);
        builder.Ignore(t => t.Entries);
        builder.Ignore(t => t.Source);

        // Backing fields for Money value object (Amount)
        builder.Property<decimal>("_amountValue").HasColumnName("amount").HasPrecision(18, 4).IsRequired();
        builder.Property<string>("_currencyCode").HasColumnName("currency_code").HasMaxLength(3).IsRequired();

        // Backing fields for OriginalAmount value object
        builder.Property<decimal?>("_originalAmountValue").HasColumnName("original_amount").HasPrecision(18, 4);
        builder.Property<string?>("_originalCurrencyCode").HasColumnName("original_currency").HasMaxLength(3);

        // Backing fields for CounterpartyInfo value object
        builder.Property<string?>("_counterpartyName").HasColumnName("counterparty_name").HasMaxLength(200);
        builder.Property<string?>("_counterpartyAccount").HasColumnName("counterparty_account").HasMaxLength(34);
        builder.Property<string?>("_counterpartyBank").HasColumnName("counterparty_bank").HasMaxLength(100);
        builder.Property<string?>("_counterpartyReference").HasColumnName("counterparty_reference").HasMaxLength(100);
    }
}

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("financial_accounts");
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.AccountNumber, a.InstitutionId })
            .IsUnique()
            .HasDatabaseName("uq_account_institution");

        builder.Property(a => a.AccountNumber).HasMaxLength(34).IsRequired();
        builder.Property(a => a.AccountName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Type).HasColumnName("account_type").IsRequired();
        builder.Property(a => a.Version).IsConcurrencyToken().HasDefaultValue(1);

        // Ignorar complex/navigation properties
        builder.Ignore(a => a.Currency);
        builder.Ignore(a => a.CurrentBalance);
        builder.Ignore(a => a.AvailableBalance);
        builder.Ignore(a => a.DomainEvents);
        builder.Ignore(a => a.DailyBalances);

        // Backing fields mapped to columns
        builder.Property<string>("_currencyCode").HasColumnName("currency_code").HasMaxLength(3);
        builder.Property<decimal>("_currentBalanceValue").HasColumnName("current_balance").HasPrecision(18, 4);
        builder.Property<decimal>("_availableBalanceValue").HasColumnName("available_balance").HasPrecision(18, 4);
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
        builder.Property(d => d.TotalDebits).HasPrecision(18, 4).IsRequired();
        builder.Property(d => d.TotalCredits).HasPrecision(18, 4).IsRequired();
    }
}

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.FromCurrency, e.ToCurrency, e.EffectiveDate })
            .IsUnique()
            .HasDatabaseName("ix_exchange_rates_currencies_date");

        builder.Property(e => e.FromCurrency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.ToCurrency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Rate).HasPrecision(18, 8).IsRequired();
        builder.Property(e => e.InverseRate).HasPrecision(18, 8);
        builder.Property(e => e.Source).HasMaxLength(50);
    }
}

public class InstitutionConfiguration : IEntityTypeConfiguration<Institution>
{
    public void Configure(EntityTypeBuilder<Institution> builder)
    {
        builder.HasKey(i => i.Id);

        builder.HasIndex(i => i.Code).IsUnique().HasDatabaseName("ix_institutions_code");

        builder.Property(i => i.Code).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Name).HasMaxLength(100).IsRequired();
        builder.Property(i => i.CountryCode).HasMaxLength(2);
        builder.Property(i => i.SwiftCode).HasMaxLength(11);
        
        // Ignorar Metadata (Dictionary no soportado directamente)
        builder.Ignore(i => i.Metadata);
    }
}
