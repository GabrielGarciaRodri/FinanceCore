using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Dashboard;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceCore.Infrastructure.BackgroundJobs.Configuration;

/// <summary>
/// Configuración de Hangfire para jobs financieros.
/// </summary>
public static class HangfireJobsConfiguration
{
    /// <summary>
    /// Configura Hangfire con PostgreSQL.
    /// </summary>
    public static IServiceCollection AddHangfireServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("HangfireConnection") 
            ?? configuration.GetConnectionString("DefaultConnection");

        services.AddHangfire(config =>
        {
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(options =>
                {
                    options.UseNpgsqlConnection(connectionString);
                }, new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    QueuePollInterval = TimeSpan.FromSeconds(15),
                    JobExpirationCheckInterval = TimeSpan.FromHours(1),
                    CountersAggregateInterval = TimeSpan.FromMinutes(5),
                    PrepareSchemaIfNecessary = true,
                    TransactionSynchronisationTimeout = TimeSpan.FromMinutes(5),
                    InvisibilityTimeout = TimeSpan.FromMinutes(30)
                });
        });

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = Environment.ProcessorCount * 2;
            options.Queues = new[] { "critical", "default", "low" };
            options.ServerName = $"FinanceCore-{Environment.MachineName}";
            options.SchedulePollingInterval = TimeSpan.FromSeconds(15);
        });

        // Registrar jobs
        services.AddScoped<Jobs.TransactionIngestionJob>();
        services.AddScoped<Jobs.DailyCloseJob>();
        services.AddScoped<Jobs.DailyReconciliationJob>();
        services.AddScoped<Jobs.ExchangeRateUpdateJob>();

        return services;
    }

    /// <summary>
    /// Configura los jobs recurrentes.
    /// </summary>
    public static void ConfigureRecurringJobs()
    {
        // Jobs de ingesta - cada 15 minutos
        RecurringJob.AddOrUpdate<Jobs.TransactionIngestionJob>(
            "transaction-file-ingestion",
            job => job.ProcessPendingFilesAsync(CancellationToken.None),
            "*/15 * * * *",
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota"),
                MisfireHandling = MisfireHandlingMode.Ignorable
            });

        // Cierre diario a las 23:59
        RecurringJob.AddOrUpdate<Jobs.DailyCloseJob>(
            "daily-close",
            job => job.ExecuteDailyCloseAsync(
                DateOnly.FromDateTime(DateTime.Today),
                CancellationToken.None),
            "59 23 * * *",
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota"),
                MisfireHandling = MisfireHandlingMode.Relaxed
            });

        // Actualizar tipos de cambio cada hora
        RecurringJob.AddOrUpdate<Jobs.ExchangeRateUpdateJob>(
            "exchange-rate-update",
            job => job.UpdateExchangeRatesAsync(CancellationToken.None),
            "0 8-18 * * 1-5",
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"),
                MisfireHandling = MisfireHandlingMode.Ignorable
            });

        // Limpieza de datos - Domingos a las 3am
        RecurringJob.AddOrUpdate<DataCleanupJob>(
            "data-cleanup",
            job => job.CleanupOldDataAsync(CancellationToken.None),
            "0 3 * * 0",
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota")
            });
    }

    public static string ScheduleReconciliation(Guid accountId, DateOnly reconciliationDate, TimeSpan? delay = null)
    {
        if (delay.HasValue)
        {
            return BackgroundJob.Schedule<Jobs.DailyReconciliationJob>(
                job => job.ReconcileAccountAsync(accountId, reconciliationDate, CancellationToken.None),
                delay.Value);
        }
        
        return BackgroundJob.Enqueue<Jobs.DailyReconciliationJob>(
            job => job.ReconcileAccountAsync(accountId, reconciliationDate, CancellationToken.None));
    }

    public static string ScheduleDailyClose(DateOnly closeDate)
    {
        return BackgroundJob.Enqueue<Jobs.DailyCloseJob>(
            job => job.ExecuteDailyCloseAsync(closeDate, CancellationToken.None));
    }
}

public class DataCleanupJob
{
    private readonly ILogger<DataCleanupJob> _logger;

    public DataCleanupJob(ILogger<DataCleanupJob> logger)
    {
        _logger = logger;
    }

    public async Task CleanupOldDataAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Iniciando limpieza de datos antiguos");
        await Task.CompletedTask;
        _logger.LogInformation("Limpieza completada");
    }
}

public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
            return true;
        
        return httpContext.User.Identity?.IsAuthenticated == true &&
               httpContext.User.IsInRole("FinanceAdmin");
    }
}
