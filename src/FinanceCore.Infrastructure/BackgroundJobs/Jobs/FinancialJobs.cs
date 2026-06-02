using System.Diagnostics;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinanceCore.Domain.Repositories;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Exceptions;
using FinanceCore.Application.Transactions.Commands.IngestTransactions;
using FinanceCore.Infrastructure.ExchangeRates;
using FinanceCore.Infrastructure.Observability;

namespace FinanceCore.Infrastructure.BackgroundJobs.Jobs;

/// <summary>
/// Job de ingesta de transacciones desde archivos.
/// Se ejecuta periódicamente para procesar archivos depositados en una ubicación.
/// </summary>
public class TransactionIngestionJob
{
    private readonly IMediator _mediator;
    private readonly IFileIngestionService _fileService;
    private readonly ILogger<TransactionIngestionJob> _logger;
    private readonly IngestionMetrics _metrics;

    public TransactionIngestionJob(
        IMediator mediator,
        IFileIngestionService fileService,
        IngestionMetrics metrics,
        ILogger<TransactionIngestionJob> logger)
    {
        _mediator = mediator;
        _fileService = fileService;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Procesa archivos de transacciones pendientes.
    /// Configurado para ejecutarse cada 15 minutos.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ProcessPendingFilesAsync(CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        var startedAt = DateTimeOffset.UtcNow;
        var filesProcessed = 0;
        var filesFailed = 0;
        var totalRowsRead = 0;
        var totalSucceeded = 0;
        var totalFailedRows = 0;
        var totalDuplicates = 0;
        
        _logger.LogInformation("[Job:{JobId}] Iniciando ingesta de archivos", jobId);

        try
        {
            var pendingFiles = await _fileService.GetPendingFilesAsync(cancellationToken);
            
            if (!pendingFiles.Any())
            {
                _logger.LogDebug("[Job:{JobId}] No hay archivos pendientes", jobId);
                return;
            }

            _logger.LogInformation(
                "[Job:{JobId}] Procesando {Count} archivos", 
                jobId, pendingFiles.Count);

            foreach (var file in pendingFiles)
            {
                var fileStopwatch = Stopwatch.StartNew();
                try
                {
                    var metrics = await ProcessFileAsync(file, jobId, cancellationToken);
                    filesProcessed++;
                    totalRowsRead += metrics.TotalRows;
                    totalSucceeded += metrics.Succeeded;
                    totalFailedRows += metrics.Failed;
                    totalDuplicates += metrics.Duplicates;

                    _metrics.FilesProcessed.Add(1, new KeyValuePair<string, object?>("file_type", file.FileType.ToString()));
                    _metrics.RowsIngested.Add(metrics.Succeeded);
                    _metrics.RowsRejected.Add(metrics.Failed);
                    _metrics.DuplicatesDetected.Add(metrics.Duplicates);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[Job:{JobId}] Error procesando archivo {FileName}",
                        jobId, file.FileName);

                    await _fileService.MoveToErrorAsync(file, ex.Message, cancellationToken);
                    filesFailed++;
                    _metrics.FilesFailed.Add(1, new KeyValuePair<string, object?>("file_type", file.FileType.ToString()));
                }
                finally
                {
                    fileStopwatch.Stop();
                    _metrics.FileProcessingDurationMs.Record(
                        fileStopwatch.Elapsed.TotalMilliseconds,
                        new KeyValuePair<string, object?>("file_type", file.FileType.ToString()));
                }
            }
            var duration = DateTimeOffset.UtcNow - startedAt;
            _logger.LogInformation(
                "[Job:{JobId}] Fin ingesta. FilesProcessed={FilesProcessed}, FilesFailed={FilesFailed}, " +
                "RowsRead={RowsRead}, Succeeded={Succeeded}, Failed={Failed}, Duplicates={Duplicates}, DurationMs={DurationMs}",
                jobId,
                filesProcessed,
                filesFailed,
                totalRowsRead,
                totalSucceeded,
                totalFailedRows,
                totalDuplicates,
                duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Job:{JobId}] Error fatal en ingesta", jobId);
            throw;
        }
    }

    private async Task<FileIngestionMetrics> ProcessFileAsync(
        PendingFile file, 
        string jobId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Job:{JobId}] Procesando archivo: {FileName} ({Size} bytes)",
            jobId, file.FileName, file.Size);

        // Parsear archivo según su tipo
        var transactions = file.FileType switch
        {
            FileType.Csv => await _fileService.ParseCsvAsync(file, cancellationToken),
            FileType.Excel => await _fileService.ParseExcelAsync(file, cancellationToken),
            _ => throw new NotSupportedException($"Tipo de archivo no soportado: {file.FileType}")
        };

        if (!transactions.Any())
        {
            _logger.LogWarning(
                "[Job:{JobId}] Archivo {FileName} sin transacciones válidas. Se moverá a error.",
                jobId, file.FileName);
            
            await _fileService.MoveToErrorAsync(file, "Archivo sin transacciones válidas", cancellationToken);
            return new FileIngestionMetrics(0, 0, 0, 0);
        }

        // Enviar comando de ingesta
        var command = new IngestTransactionsCommand
        {
            Source = $"FILE_{file.FileName}",
            SourceType = file.FileType == FileType.Csv ? SourceType.CsvFile : SourceType.ExcelFile,
            Transactions = transactions,
            Metadata = new Dictionary<string, object>
            {
                ["FileName"] = file.FileName,
                ["FileSize"] = file.Size,
                ["ProcessedAt"] = DateTimeOffset.UtcNow
            }
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsSuccess)
        {
            var value = result.GetValueOrThrow();
            _logger.LogInformation(
                "[Job:{JobId}] Archivo {FileName} procesado. " +
                "Éxitos: {Success}, Fallos: {Failed}, Duplicados: {Duplicates}",
                jobId, file.FileName,
                value.Succeeded,
                value.Failed,
                value.Duplicates);

            await _fileService.MoveToProcessedAsync(file, cancellationToken);
            return new FileIngestionMetrics(
                value.TotalReceived,
                value.Succeeded,
                value.Failed,
                value.Duplicates);
        }
        _logger.LogError(
            "[Job:{JobId}] Error procesando archivo {FileName}: {Error}",
            jobId, file.FileName, result.Error);
        await _fileService.MoveToErrorAsync(file, result.Error!, cancellationToken);
        return new FileIngestionMetrics(transactions.Count, 0, transactions.Count, 0);
    }
}
public record FileIngestionMetrics(int TotalRows, int Succeeded, int Failed, int Duplicates);

/// <summary>
/// Job de cierre diario de cuentas.
/// Calcula balances, genera registros diarios y detecta descuadres.
/// </summary>
public class DailyCloseJob
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DailyCloseJob> _logger;
    private readonly Infrastructure.Reconciliations.ReconciliationOptions _reconciliationOptions;

    public DailyCloseJob(
        IUnitOfWork unitOfWork,
        IOptions<Infrastructure.Reconciliations.ReconciliationOptions> reconciliationOptions,
        ILogger<DailyCloseJob> logger)
    {
        _unitOfWork = unitOfWork;
        _reconciliationOptions = reconciliationOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta el cierre diario de todas las cuentas activas.
    /// Se programa para las 23:59 del día.
    /// </summary>
    [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 60, 120, 300, 600, 1200 })]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [Queue("critical")]
    public async Task ExecuteDailyCloseAsync(
        DateOnly closeDate,
        CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogInformation(
            "[DailyClose:{JobId}] Iniciando cierre para fecha {Date}",
            jobId, closeDate);

        var accounts = await _unitOfWork.Accounts.GetActiveAccountsAsync(cancellationToken);
        var processed = 0;
        var errors = new List<string>();
        var accountsToReconcile = new List<Guid>();

        foreach (var account in accounts)
        {
            try
            {
                await ProcessAccountCloseAsync(account.Id, closeDate, cancellationToken);
                processed++;
                accountsToReconcile.Add(account.Id);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error cerrando cuenta {account.AccountNumber}: {ex.Message}";
                errors.Add(errorMsg);
                _logger.LogError(ex, "[DailyClose:{JobId}] {Error}", jobId, errorMsg);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[DailyClose:{JobId}] Cierre completado. Procesadas: {Processed}/{Total}, Errores: {Errors}",
            jobId, processed, accounts.Count, errors.Count);

        if (_reconciliationOptions.AutoReconcileAfterClose && accountsToReconcile.Count > 0)
        {
            foreach (var accId in accountsToReconcile)
            {
                Hangfire.BackgroundJob.Enqueue<DailyReconciliationJob>(
                    j => j.ReconcileAccountAsync(accId, closeDate, CancellationToken.None));
            }

            _logger.LogInformation(
                "[DailyClose:{JobId}] Encoladas {Count} conciliaciones automáticas para {Date}",
                jobId, accountsToReconcile.Count, closeDate);
        }

        if (errors.Any())
        {
            // En producción: enviar alerta, notificación, etc.
            _logger.LogWarning(
                "[DailyClose:{JobId}] Errores en cierre: {Errors}",
                jobId, string.Join("; ", errors));
        }
    }

    private async Task ProcessAccountCloseAsync(
        Guid accountId,
        DateOnly closeDate,
        CancellationToken cancellationToken)
    {
        // Obtener balance anterior
        var previousBalance = await _unitOfWork.DailyBalances
            .GetByAccountAndDateAsync(accountId, closeDate.AddDays(-1), cancellationToken);

        var openingBalance = previousBalance?.ClosingBalance ?? 0m;

        // Obtener transacciones del día
        var transactions = await _unitOfWork.Transactions
            .GetByAccountAndDateRangeAsync(accountId, closeDate, closeDate, cancellationToken);

        // Calcular totales
        var postedTransactions = transactions
            .Where(t => t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Reconciled)
            .ToList();

        var totalDebits = postedTransactions
            .Where(t => t.Amount.Amount < 0)
            .Sum(t => t.Amount.Amount);

        var totalCredits = postedTransactions
            .Where(t => t.Amount.Amount > 0)
            .Sum(t => t.Amount.Amount);

        var closingBalance = openingBalance + totalDebits + totalCredits;

        // Crear o actualizar balance diario
        var dailyBalance = await _unitOfWork.DailyBalances
            .GetByAccountAndDateAsync(accountId, closeDate, cancellationToken);

        if (dailyBalance == null)
        {
            dailyBalance = new Domain.Entities.DailyBalance
            {
                AccountId = accountId,
                BalanceDate = closeDate,
                OpeningBalance = openingBalance,
                ClosingBalance = closingBalance,
                TotalDebits = totalDebits,
                TotalCredits = totalCredits,
                TransactionCount = postedTransactions.Count
            };
        }
        else
        {
            dailyBalance.Update(
                openingBalance,
                closingBalance,
                totalDebits,
                totalCredits,
                postedTransactions.Count);
        }

        await _unitOfWork.DailyBalances.UpsertAsync(dailyBalance, cancellationToken);

        // Verificar consistencia con saldo de la cuenta
        var account = await _unitOfWork.Accounts.GetByIdAsync(accountId, cancellationToken);
        if (account != null && Math.Abs(account.CurrentBalance.Amount - closingBalance) > 0.001m)
        {
            _logger.LogWarning(
                "Descuadre detectado en cuenta {AccountId}. " +
                "Balance cuenta: {AccountBalance}, Balance calculado: {CalculatedBalance}",
                accountId, account.CurrentBalance.Amount, closingBalance);
            
            // Aquí se podría disparar una alerta o crear una tarea de revisión
        }
    }
}

/// <summary>
/// Job de conciliación diaria automática.
/// </summary>
public class DailyReconciliationJob
{
    private readonly IReconciliationEngine _reconciliationEngine;
    private readonly ILogger<DailyReconciliationJob> _logger;
    private readonly Infrastructure.Reconciliations.ReconciliationOptions _options;

    public DailyReconciliationJob(
        IReconciliationEngine reconciliationEngine,
        IOptions<Infrastructure.Reconciliations.ReconciliationOptions> options,
        ILogger<DailyReconciliationJob> logger)
    {
        _reconciliationEngine = reconciliationEngine;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta conciliación automática (balance-based) para una cuenta y fecha.
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task ReconcileAccountAsync(
        Guid accountId,
        DateOnly reconciliationDate,
        CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        var accountRef = accountId.ToString("N")[..8];

        _logger.LogInformation(
            "[Reconciliation:{JobId}] Iniciando conciliación cuenta {AccountRef} fecha {Date}",
            jobId, accountRef, reconciliationDate);

        try
        {
            var result = await _reconciliationEngine.ReconcileAsync(
                accountId,
                reconciliationDate,
                cancellationToken);

            _logger.LogInformation(
                "[Reconciliation:{JobId}] Completada. ReconciliationId={ReconciliationId}, " +
                "Matched={Matched}, UnmatchedInternal={UnmatchedInternal}, UnmatchedExternal={UnmatchedExternal}, " +
                "Discrepancy={Discrepancy}, DiscrepancyCount={DiscrepancyCount}, Status={Status}",
                jobId,
                result.ReconciliationId,
                result.MatchedCount,
                result.UnmatchedInternal,
                result.UnmatchedExternal,
                result.DiscrepancyAmount,
                result.DiscrepancyCount,
                result.Status);

            if (result.HasDiscrepancies &&
                Math.Abs(result.DiscrepancyAmount) >= _options.SignificantDiscrepancyThreshold)
            {
                _logger.LogWarning(
                    "[Reconciliation:{JobId}] ALERTA: Descuadre significativo de {Amount} en cuenta {AccountRef}",
                    jobId, result.DiscrepancyAmount, accountRef);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Reconciliation:{JobId}] Error en conciliación cuenta {AccountRef}",
                jobId, accountRef);
            throw;
        }
    }
}

/// <summary>
/// Job de actualización de tipos de cambio.
/// </summary>
public class ExchangeRateUpdateJob
{
    private readonly IExchangeRateProvider _provider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ExchangeRateOptions _options;
    private readonly ExchangeRateMetrics _metrics;
    private readonly ILogger<ExchangeRateUpdateJob> _logger;

    public ExchangeRateUpdateJob(
        IExchangeRateProvider provider,
        IUnitOfWork unitOfWork,
        IOptions<ExchangeRateOptions> options,
        ExchangeRateMetrics metrics,
        ILogger<ExchangeRateUpdateJob> logger)
    {
        _provider = provider;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Actualiza tipos de cambio desde proveedor externo.
    /// Se ejecuta cada hora durante el día. Fallback silencioso si el proveedor falla.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task UpdateExchangeRatesAsync(CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("[ExchangeRate:{JobId}] Actualizando tipos de cambio", jobId);

        var baseCurrency = _options.BaseCurrency;
        var currencies = _options.SupportedCurrencies;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        IEnumerable<ExchangeRateData> rates;

        var providerStopwatch = Stopwatch.StartNew();
        try
        {
            rates = await _provider.GetLatestRatesAsync(baseCurrency, currencies, cancellationToken);
            providerStopwatch.Stop();
            _metrics.ProviderCalls.Add(1, new KeyValuePair<string, object?>("provider", _provider.ProviderName));
            _metrics.ProviderLatencyMs.Record(
                providerStopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("provider", _provider.ProviderName));
        }
        catch (ExchangeRateProviderException ex)
        {
            providerStopwatch.Stop();
            _metrics.ProviderFailures.Add(1, new KeyValuePair<string, object?>("provider", _provider.ProviderName));
            _metrics.ProviderLatencyMs.Record(
                providerStopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("provider", _provider.ProviderName));
            _logger.LogWarning(
                "[ExchangeRate:{JobId}] Proveedor no disponible — usando rates existentes en DB. Motivo: {Message}",
                jobId, ex.Message);
            return;
        }

         try
        {
            foreach (var rate in rates)
            {
                var exchangeRate = new Domain.Entities.ExchangeRate
                {
                    FromCurrency = rate.FromCurrency,
                    ToCurrency = rate.ToCurrency,
                    Rate = rate.Rate,
                    InverseRate = rate.Rate > 0 ? Math.Round(1m / rate.Rate, 8) : 0,
                    EffectiveDate = today,
                    Source = _provider.ProviderName
                };
                
                await _unitOfWork.ExchangeRates.AddAsync(exchangeRate, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var ratesCount = rates.Count();
            _metrics.RatesUpserted.Add(
                ratesCount,
                new KeyValuePair<string, object?>("provider", _provider.ProviderName));

            _logger.LogInformation(
                "[ExchangeRate:{JobId}] Actualizados {Count} tipos de cambio desde {Provider}",
                jobId, ratesCount, _provider.ProviderName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ExchangeRate:{JobId}] Error persistiendo tipos de cambio", jobId);
            throw;
        }
    }
}

#region Interfaces de soporte

public interface IFileIngestionService
{
    Task<IReadOnlyList<PendingFile>> GetPendingFilesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionDto>> ParseCsvAsync(PendingFile file, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionDto>> ParseExcelAsync(PendingFile file, CancellationToken cancellationToken);
    Task MoveToProcessedAsync(PendingFile file, CancellationToken cancellationToken);
    Task MoveToErrorAsync(PendingFile file, string error, CancellationToken cancellationToken);
}

public interface IReconciliationEngine
{
    /// <summary>
    /// Concilia una cuenta y fecha contra el balance reportado (DailyBalance.ClosingBalance).
    /// </summary>
    Task<ReconciliationResult> ReconcileAsync(
        Guid accountId,
        DateOnly date,
        CancellationToken cancellationToken);

    /// <summary>
    /// Concilia una cuenta y fecha contra una lista de transacciones externas (extracto).
    /// </summary>
    Task<ReconciliationResult> ReconcileWithStatementAsync(
        Guid accountId,
        DateOnly date,
        IReadOnlyList<Domain.Entities.ExternalStatementLine> statementLines,
        CancellationToken cancellationToken);
}

public interface IExchangeRateProvider
{
    string ProviderName { get; }
    Task<IEnumerable<ExchangeRateData>> GetLatestRatesAsync(
        string baseCurrency,
        string[] targetCurrencies,
        CancellationToken cancellationToken);
}

public record PendingFile(string FileName, string FullPath, long Size, FileType FileType);
public enum FileType { Csv, Excel }
public record ExchangeRateData(string FromCurrency, string ToCurrency, decimal Rate);
public record ReconciliationResult(
    Guid ReconciliationId,
    int MatchedCount,
    int UnmatchedInternal,
    int UnmatchedExternal,
    decimal DiscrepancyAmount,
    bool HasDiscrepancies,
    Domain.Enums.ReconciliationStatus Status,
    int DiscrepancyCount);

#endregion
