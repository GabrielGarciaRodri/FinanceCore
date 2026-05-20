using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FinanceCore.Infrastructure.Observability;

/// <summary>
/// Punto único de instrumentación: meters + activity sources.
/// Importar el meter name <see cref="MeterName"/> al configurar OpenTelemetry.
/// </summary>
public static class FinanceCoreTelemetry
{
    public const string MeterName = "FinanceCore";
    public const string ActivitySourceName = "FinanceCore";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName, "1.0.0");
}

/// <summary>
/// Métricas de ingestión de archivos.
/// </summary>
public sealed class IngestionMetrics
{
    public Counter<long> FilesProcessed { get; }
    public Counter<long> FilesFailed { get; }
    public Counter<long> RowsIngested { get; }
    public Counter<long> RowsRejected { get; }
    public Counter<long> DuplicatesDetected { get; }
    public Histogram<double> FileProcessingDurationMs { get; }

    public IngestionMetrics()
    {
        FilesProcessed = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.ingestion.files_processed",
            unit: "files",
            description: "Archivos procesados exitosamente por el job de ingesta.");

        FilesFailed = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.ingestion.files_failed",
            unit: "files",
            description: "Archivos que fallaron y fueron movidos a error.");

        RowsIngested = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.ingestion.rows_ingested",
            unit: "rows",
            description: "Filas válidas insertadas como transacciones.");

        RowsRejected = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.ingestion.rows_rejected",
            unit: "rows",
            description: "Filas rechazadas por validación.");

        DuplicatesDetected = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.ingestion.duplicates",
            unit: "rows",
            description: "Filas descartadas por duplicado (ExternalId o hash).");

        FileProcessingDurationMs = FinanceCoreTelemetry.Meter.CreateHistogram<double>(
            "financecore.ingestion.file_duration",
            unit: "ms",
            description: "Tiempo de procesamiento por archivo.");
    }
}

/// <summary>
/// Métricas de reconciliación.
/// </summary>
public sealed class ReconciliationMetrics
{
    public Counter<long> RunsTotal { get; }
    public Counter<long> RunsCompletedClean { get; }
    public Counter<long> RunsCompletedWithDiscrepancies { get; }
    public Counter<long> RunsFailed { get; }
    public Counter<long> DiscrepanciesCreated { get; }
    public Histogram<double> DurationMs { get; }
    public Histogram<double> DiscrepancyAmount { get; }

    public ReconciliationMetrics()
    {
        RunsTotal = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.reconciliation.runs_total",
            unit: "runs",
            description: "Total de conciliaciones ejecutadas.");

        RunsCompletedClean = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.reconciliation.runs_clean",
            unit: "runs",
            description: "Conciliaciones que terminaron en Completed sin discrepancias.");

        RunsCompletedWithDiscrepancies = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.reconciliation.runs_with_discrepancies",
            unit: "runs",
            description: "Conciliaciones que terminaron en CompletedWithDiscrepancies.");

        RunsFailed = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.reconciliation.runs_failed",
            unit: "runs",
            description: "Conciliaciones que terminaron en Failed por excepción.");

        DiscrepanciesCreated = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.reconciliation.discrepancies",
            unit: "discrepancies",
            description: "Discrepancias generadas por tipo (etiqueta type).");

        DurationMs = FinanceCoreTelemetry.Meter.CreateHistogram<double>(
            "financecore.reconciliation.duration",
            unit: "ms",
            description: "Duración de cada corrida de reconciliación.");

        DiscrepancyAmount = FinanceCoreTelemetry.Meter.CreateHistogram<double>(
            "financecore.reconciliation.discrepancy_amount",
            unit: "amount",
            description: "Magnitud absoluta del descuadre detectado por corrida.");
    }
}

/// <summary>
/// Métricas del proveedor FX.
/// </summary>
public sealed class ExchangeRateMetrics
{
    public Counter<long> ProviderCalls { get; }
    public Counter<long> ProviderFailures { get; }
    public Counter<long> RatesUpserted { get; }
    public Histogram<double> ProviderLatencyMs { get; }

    public ExchangeRateMetrics()
    {
        ProviderCalls = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.fx.provider_calls",
            unit: "calls",
            description: "Llamadas exitosas al proveedor de FX.");

        ProviderFailures = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.fx.provider_failures",
            unit: "calls",
            description: "Llamadas fallidas al proveedor de FX (timeout, 5xx, circuit-break).");

        RatesUpserted = FinanceCoreTelemetry.Meter.CreateCounter<long>(
            "financecore.fx.rates_upserted",
            unit: "rates",
            description: "Tipos de cambio insertados/actualizados.");

        ProviderLatencyMs = FinanceCoreTelemetry.Meter.CreateHistogram<double>(
            "financecore.fx.provider_latency",
            unit: "ms",
            description: "Latencia de las llamadas al proveedor.");
    }
}
