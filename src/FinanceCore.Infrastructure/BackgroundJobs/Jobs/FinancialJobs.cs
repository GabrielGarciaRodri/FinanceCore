using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using FinanceCore.Domain.Repositories;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Exceptions;
using FinanceCore.Application.Transactions.Commands.IngestTransactions;

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

    public TransactionIngestionJob(
        IMediator mediator,
        IFileIngestionService fileService,
        ILogger<TransactionIngestionJob> logger)
    {
        _mediator = mediator;
        _fileService = fileService;
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
                try
                {
                    await ProcessFileAsync(file, jobId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, 
                        "[Job:{JobId}] Error procesando archivo {FileName}", 
                        jobId, file.FileName);
                    
                    await _fileService.MoveToErrorAsync(file, ex.Message, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Job:{JobId}] Error fatal en ingesta", jobId);
            throw;
        }
    }

    private async Task ProcessFileAsync(
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
                "[Job:{JobId}] Archivo {FileName} sin transacciones válidas",
                jobId, file.FileName);
            
            await _fileService.MoveToProcessedAsync(file, cancellationToken);
            return;
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
            _logger.LogInformation(
                "[Job:{JobId}] Archivo {FileName} procesado. " +
                "Éxitos: {Success}, Fallos: {Failed}, Duplicados: {Duplicates}",
                jobId, file.FileName, 
                result.Value.Succeeded, 
                result.Value.Failed, 
                result.Value.Duplicates);

            await _fileService.MoveToProcessedAsync(file, cancellationToken);
        }
        else
        {
            _logger.LogError(
                "[Job:{JobId}] Error procesando archivo {FileName}: {Error}",
                jobId, file.FileName, result.Error);

            await _fileService.MoveToErrorAsync(file, result.Error!, cancellationToken);
        }
    }
}

/// <summary>
/// Job de cierre diario de cuentas.
/// Calcula balances, genera registros diarios y detecta descuadres.
/// </summary>
public class DailyCloseJob
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DailyCloseJob> _logger;

    public DailyCloseJob(
        IUnitOfWork unitOfWork,
        ILogger<DailyCloseJob> logger)
    {
        _unitOfWork = unitOfWork;
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

        foreach (var account in accounts)
        {
            try
            {
                await ProcessAccountCloseAsync(account.Id, closeDate, cancellationToken);
                processed++;
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReconciliationEngine _reconciliationEngine;
    private readonly ILogger<DailyReconciliationJob> _logger;

    public DailyReconciliationJob(
        IUnitOfWork unitOfWork,
        IReconciliationEngine reconciliationEngine,
        ILogger<DailyReconciliationJob> logger)
    {
        _unitOfWork = unitOfWork;
        _reconciliationEngine = reconciliationEngine;
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta conciliación automática para una cuenta y fecha.
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task ReconcileAccountAsync(
        Guid accountId,
        DateOnly reconciliationDate,
        CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogInformation(
            "[Reconciliation:{JobId}] Iniciando conciliación cuenta {AccountId} fecha {Date}",
            jobId, accountId, reconciliationDate);

        try
        {
            var result = await _reconciliationEngine.ReconcileAsync(
                accountId,
                reconciliationDate,
                cancellationToken);

            _logger.LogInformation(
                "[Reconciliation:{JobId}] Completada. Matched: {Matched}, Unmatched: {Unmatched}, Discrepancy: {Discrepancy}",
                jobId, result.MatchedCount, result.UnmatchedCount, result.DiscrepancyAmount);

            if (result.HasDiscrepancies)
            {
                // Escalar descuadres significativos
                if (Math.Abs(result.DiscrepancyAmount) > 1000)
                {
                    _logger.LogWarning(
                        "[Reconciliation:{JobId}] ALERTA: Descuadre significativo de {Amount}",
                        jobId, result.DiscrepancyAmount);
                    
                    // Crear tarea de revisión, enviar notificación, etc.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Reconciliation:{JobId}] Error en conciliación",
                jobId);
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
    private readonly ILogger<ExchangeRateUpdateJob> _logger;

    public ExchangeRateUpdateJob(
        IExchangeRateProvider provider,
        IUnitOfWork unitOfWork,
        ILogger<ExchangeRateUpdateJob> logger)
    {
        _provider = provider;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Actualiza tipos de cambio desde proveedor externo.
    /// Se ejecuta cada hora durante el día.
    /// </summary>
    [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 30, 60, 120, 300, 600 })]
    public async Task UpdateExchangeRatesAsync(CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogInformation("[ExchangeRate:{JobId}] Actualizando tipos de cambio", jobId);

        try
        {
            // Monedas que nos interesan
            var currencies = new[] { "USD", "EUR", "COP", "MXN", "BRL" };
            var baseCurrency = "USD";
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var rates = await _provider.GetLatestRatesAsync(baseCurrency, currencies, cancellationToken);

            foreach (var rate in rates)
            {
                var exchangeRate = new Domain.Entities.ExchangeRate
                {
                    FromCurrency = rate.FromCurrency,
                    ToCurrency = rate.ToCurrency,
                    Rate = rate.Rate,
                    InverseRate = 1 / rate.Rate,
                    EffectiveDate = today,
                    Source = _provider.ProviderName
                };

                await _unitOfWork.ExchangeRates.AddAsync(exchangeRate, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[ExchangeRate:{JobId}] Actualizados {Count} tipos de cambio",
                jobId, rates.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ExchangeRate:{JobId}] Error actualizando tipos de cambio", jobId);
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
    Task<ReconciliationResult> ReconcileAsync(
        Guid accountId,
        DateOnly date,
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
    int MatchedCount, 
    int UnmatchedCount, 
    decimal DiscrepancyAmount,
    bool HasDiscrepancies);

#endregion
