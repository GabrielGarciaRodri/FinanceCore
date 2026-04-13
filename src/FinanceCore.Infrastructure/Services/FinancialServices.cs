using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinanceCore.Application.Transactions.Commands.IngestTransactions;
using Microsoft.EntityFrameworkCore;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;
using FinanceCore.Domain.ValueObjects;
using FinanceCore.Infrastructure.Persistence.Context;
using FinanceCore.Infrastructure.BackgroundJobs.Jobs;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FinanceCore.Infrastructure.Services;

#region Unit of Work

public class UnitOfWork : IUnitOfWork
{
    private readonly FinanceCoreDbContext _context;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDailyBalanceRepository _dailyBalanceRepository;
    private readonly IExchangeRateRepository _exchangeRateRepository;

    public UnitOfWork(
        FinanceCoreDbContext context,
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        IDailyBalanceRepository dailyBalanceRepository,
        IExchangeRateRepository exchangeRateRepository)
    {
        _context = context;
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _dailyBalanceRepository = dailyBalanceRepository;
        _exchangeRateRepository = exchangeRateRepository;
    }

    public ITransactionRepository Transactions => _transactionRepository;

    public IAccountRepository Accounts => _accountRepository;

    public IDailyBalanceRepository DailyBalances => _dailyBalanceRepository;

    public IExchangeRateRepository ExchangeRates => _exchangeRateRepository;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction == null)
            await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction != null)
            await _context.Database.CurrentTransaction.CommitAsync(cancellationToken);
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction != null)
            await _context.Database.CurrentTransaction.RollbackAsync(cancellationToken);
    }

    public void Dispose() => _context.Dispose();
}

#endregion

#region Account Repository

public class AccountRepository : IAccountRepository
{
    private readonly FinanceCoreDbContext _context;

    public AccountRepository(FinanceCoreDbContext context) => _context = context;

    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Accounts.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<Account>> GetAllAsync(
        System.Linq.Expressions.Expression<Func<Account, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.Accounts.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.ToListAsync(ct);
    }

    public void Add(Account entity) => _context.Accounts.Add(entity);
    public void AddRange(IEnumerable<Account> entities) => _context.Accounts.AddRange(entities);
    public void Update(Account entity) => _context.Accounts.Update(entity);
    public void Remove(Account entity) => _context.Accounts.Remove(entity);

    public async Task<bool> ExistsAsync(
        System.Linq.Expressions.Expression<Func<Account, bool>> predicate,
        CancellationToken ct = default)
        => await _context.Accounts.AnyAsync(predicate, ct);

    public async Task<int> CountAsync(
        System.Linq.Expressions.Expression<Func<Account, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.Accounts.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.CountAsync(ct);
    }

    public async Task<Account?> GetByAccountNumberAsync(string accountNumber, Guid institutionId, CancellationToken ct = default)
        => await _context.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == accountNumber && a.InstitutionId == institutionId, ct);

    public async Task<IReadOnlyList<Account>> GetActiveAccountsAsync(CancellationToken ct = default)
        => await _context.Accounts.Where(a => a.IsActive).ToListAsync(ct);

    public async Task<IReadOnlyList<Account>> GetByInstitutionAsync(Guid institutionId, CancellationToken ct = default)
        => await _context.Accounts.Where(a => a.InstitutionId == institutionId).ToListAsync(ct);

    public async Task<Account?> GetByIdWithLockAsync(Guid id, int expectedVersion, CancellationToken ct = default)
        => await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.Version == expectedVersion, ct);

    public async Task<IReadOnlyDictionary<string, decimal>> GetTotalBalancesByCurrencyAsync(CancellationToken ct = default)
    {
        var balances = await _context.Accounts
            .Where(a => a.IsActive)
            .GroupBy(a => a.Currency.Code)
            .Select(g => new { Currency = g.Key, Total = g.Sum(a => a.CurrentBalance.Amount) })
            .ToListAsync(ct);
        
        return balances.ToDictionary(b => b.Currency, b => b.Total);
    }
}

#endregion

#region DailyBalance Repository

public class DailyBalanceRepository : IDailyBalanceRepository
{
    private readonly FinanceCoreDbContext _context;

    public DailyBalanceRepository(FinanceCoreDbContext context) => _context = context;

    public async Task<DailyBalance?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.DailyBalances.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<DailyBalance>> GetAllAsync(
        System.Linq.Expressions.Expression<Func<DailyBalance, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.DailyBalances.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.ToListAsync(ct);
    }

    public void Add(DailyBalance entity) => _context.DailyBalances.Add(entity);
    public void AddRange(IEnumerable<DailyBalance> entities) => _context.DailyBalances.AddRange(entities);
    public void Update(DailyBalance entity) => _context.DailyBalances.Update(entity);
    public void Remove(DailyBalance entity) => _context.DailyBalances.Remove(entity);

    public async Task<bool> ExistsAsync(
        System.Linq.Expressions.Expression<Func<DailyBalance, bool>> predicate,
        CancellationToken ct = default)
        => await _context.DailyBalances.AnyAsync(predicate, ct);

    public async Task<int> CountAsync(
        System.Linq.Expressions.Expression<Func<DailyBalance, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.DailyBalances.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.CountAsync(ct);
    }

    public async Task<DailyBalance?> GetByAccountAndDateAsync(Guid accountId, DateOnly date, CancellationToken ct = default)
        => await _context.DailyBalances.FirstOrDefaultAsync(d => d.AccountId == accountId && d.BalanceDate == date, ct);

    public async Task<IReadOnlyList<DailyBalance>> GetByAccountAndDateRangeAsync(
        Guid accountId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
        => await _context.DailyBalances
            .Where(d => d.AccountId == accountId && d.BalanceDate >= startDate && d.BalanceDate <= endDate)
            .OrderBy(d => d.BalanceDate)
            .ToListAsync(ct);

    public async Task<DailyBalance?> GetLatestByAccountAsync(Guid accountId, CancellationToken ct = default)
        => await _context.DailyBalances
            .Where(d => d.AccountId == accountId)
            .OrderByDescending(d => d.BalanceDate)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<DailyBalance>> GetUnreconciledAsync(DateOnly? beforeDate = null, CancellationToken ct = default)
    {
        var query = _context.DailyBalances.Where(d => !d.IsReconciled);
        if (beforeDate.HasValue) query = query.Where(d => d.BalanceDate < beforeDate.Value);
        return await query.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DailyBalance>> GetMissingDatesAsync(
        Guid accountId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        if (endDate < startDate)
            throw new ArgumentException("endDate debe ser mayor o igual a startDate.");

        var existingDates = await _context.DailyBalances
            .Where(d => d.AccountId == accountId && d.BalanceDate >= startDate && d.BalanceDate <= endDate)
            .Select(d => d.BalanceDate)
            .ToListAsync(ct);

        var existingSet = existingDates.ToHashSet();
        var missing = new List<DailyBalance>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            ct.ThrowIfCancellationRequested();
            if (existingSet.Contains(date))
                continue;

            missing.Add(new DailyBalance
            {
                AccountId = accountId,
                BalanceDate = date,
                OpeningBalance = 0m,
                ClosingBalance = 0m,
                TotalDebits = 0m,
                TotalCredits = 0m,
                TransactionCount = 0,
                IsReconciled = false
            });
        }

        return missing;
    }

    public async Task UpsertAsync(DailyBalance balance, CancellationToken ct = default)
    {
        var existing = await GetByAccountAndDateAsync(balance.AccountId, balance.BalanceDate, ct);
        if (existing != null)
        {
            existing.Update(balance.OpeningBalance, balance.ClosingBalance, balance.TotalDebits, balance.TotalCredits, balance.TransactionCount);
            Update(existing);
        }
        else
        {
            Add(balance);
        }
    }
}

#endregion

#region ExchangeRate Repository

public class ExchangeRateRepository : IExchangeRateRepository
{
    private readonly FinanceCoreDbContext _context;

    public ExchangeRateRepository(FinanceCoreDbContext context) => _context = context;

    public async Task<ExchangeRate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.ExchangeRates.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<ExchangeRate>> GetAllAsync(
        System.Linq.Expressions.Expression<Func<ExchangeRate, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.ExchangeRates.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.ToListAsync(ct);
    }

    public void Add(ExchangeRate entity) => _context.ExchangeRates.Add(entity);
    public void AddRange(IEnumerable<ExchangeRate> entities) => _context.ExchangeRates.AddRange(entities);
    public void Update(ExchangeRate entity) => _context.ExchangeRates.Update(entity);
    public void Remove(ExchangeRate entity) => _context.ExchangeRates.Remove(entity);

    public async Task<bool> ExistsAsync(
        System.Linq.Expressions.Expression<Func<ExchangeRate, bool>> predicate,
        CancellationToken ct = default)
        => await _context.ExchangeRates.AnyAsync(predicate, ct);

    public async Task<int> CountAsync(
        System.Linq.Expressions.Expression<Func<ExchangeRate, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.ExchangeRates.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.CountAsync(ct);
    }

    public async Task<ExchangeRate?> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default)
        => await _context.ExchangeRates
            .Where(e => e.FromCurrency == from && e.ToCurrency == to && e.EffectiveDate <= date)
            .OrderByDescending(e => e.EffectiveDate)
            .FirstOrDefaultAsync(ct);

    public async Task<ExchangeRate?> GetLatestRateAsync(string from, string to, CancellationToken ct = default)
        => await _context.ExchangeRates
            .Where(e => e.FromCurrency == from && e.ToCurrency == to)
            .OrderByDescending(e => e.EffectiveDate)
            .FirstOrDefaultAsync(ct);

    public async Task<decimal> ConvertAsync(decimal amount, string from, string to, DateOnly date, CancellationToken ct = default)
    {
        if (from == to) return amount;
        var rate = await GetRateAsync(from, to, date, ct);
        if (rate == null) throw new InvalidOperationException($"Exchange rate not found: {from} to {to}");
        return Math.Round(amount * rate.Rate, 4, MidpointRounding.ToEven);
    }

    public async Task<IReadOnlyList<ExchangeRate>> GetHistoricalRatesAsync(
        string from, string to, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
        => await _context.ExchangeRates
            .Where(e => e.FromCurrency == from && e.ToCurrency == to && e.EffectiveDate >= startDate && e.EffectiveDate <= endDate)
            .OrderBy(e => e.EffectiveDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExchangeRate>> GetAllLatestRatesAsync(string baseCurrency, CancellationToken ct = default)
        => await _context.ExchangeRates
            .Where(e => e.FromCurrency == baseCurrency)
            .GroupBy(e => e.ToCurrency)
            .Select(g => g.OrderByDescending(e => e.EffectiveDate).First())
            .ToListAsync(ct);

    public async Task AddAsync(ExchangeRate rate, CancellationToken ct = default)
        => await _context.ExchangeRates.AddAsync(rate, ct);

    public async Task AddRangeAsync(IEnumerable<ExchangeRate> rates, CancellationToken ct = default)
        => await _context.ExchangeRates.AddRangeAsync(rates, ct);

    public async Task<IReadOnlyList<ExchangeRate>> GetHistoryAsync(
        string from, string to, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
        => await GetHistoricalRatesAsync(from, to, startDate, endDate, ct);
}

#endregion

#region File Ingestion Service

public class FileIngestionOptions
{
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

    public string InputDirectory { get; set; } = "./data/input";
    public string ProcessedDirectory { get; set; } = "./data/processed";
    public string ErrorDirectory { get; set; } = "./data/error";
    public string[] SupportedExtensions { get; set; } = [".csv", ".xlsx"];
    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;
    public string FileNamePattern { get; set; } = @"^[A-Za-z0-9._-]+$";
}

public class FileIngestionService : IFileIngestionService
{
    private readonly ILogger<FileIngestionService> _logger;
    private readonly FileIngestionOptions _options;
    private readonly HashSet<string> _supportedExtensions;
    private readonly Regex _fileNameRegex;
    private static readonly StringComparer CaseInsensitiveHeaderComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> ValidTransactionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "debit", "credit", "transfer_out", "transfer_in", "fee", "interest", "adjustment"
    };
    private const int MaxMoveRetryAttempts = 3;
    private const int Iso4217CurrencyCodeLength = 3;

    public FileIngestionService(
        ILogger<FileIngestionService> logger,
        IOptions<FileIngestionOptions> options)
    {
        _logger = logger;
        _options = options.Value ?? new FileIngestionOptions();

        _options.InputDirectory = Path.GetFullPath(_options.InputDirectory ?? "./data/input");
        _options.ProcessedDirectory = Path.GetFullPath(_options.ProcessedDirectory ?? "./data/processed");
        _options.ErrorDirectory = Path.GetFullPath(_options.ErrorDirectory ?? "./data/error");

        _supportedExtensions = (_options.SupportedExtensions ?? Array.Empty<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : $".{e.ToLowerInvariant()}")
            .ToHashSet();

        if (_supportedExtensions.Count == 0)
        {
            _supportedExtensions = [".csv", ".xlsx"];
        }

        _fileNameRegex = new Regex(
            string.IsNullOrWhiteSpace(_options.FileNamePattern) ? @"^[A-Za-z0-9._-]+$" : _options.FileNamePattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    public async Task<IReadOnlyList<PendingFile>> GetPendingFilesAsync(CancellationToken ct = default)
    {
        EnsureDirectoriesExist();

        var candidates = Directory.EnumerateFiles(_options.InputDirectory, "*", System.IO.SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderBy(f => f.CreationTimeUtc)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pending = new List<PendingFile>(candidates.Count);

        foreach (var file in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var extension = file.Extension.ToLowerInvariant();
            if (!_supportedExtensions.Contains(extension))
            {
                await MoveToErrorAsync(
                    new PendingFile(file.Name, file.FullName, file.Length, InferFileType(extension)),
                    $"Extensión no soportada: {extension}",
                    ct);
                continue;
            }

            if (file.Length == 0)
            {
                await MoveToErrorAsync(
                    new PendingFile(file.Name, file.FullName, file.Length, InferFileType(extension)),
                    "Archivo vacío",
                    ct);
                continue;
            }

            if (_options.MaxFileSizeBytes > 0 && file.Length > _options.MaxFileSizeBytes)
            {
                await MoveToErrorAsync(
                    new PendingFile(file.Name, file.FullName, file.Length, InferFileType(extension)),
                    $"Archivo excede tamaño máximo permitido ({_options.MaxFileSizeBytes} bytes)",
                    ct);
                continue;
            }

            if (!_fileNameRegex.IsMatch(file.Name))
            {
                await MoveToErrorAsync(
                    new PendingFile(file.Name, file.FullName, file.Length, InferFileType(extension)),
                    "Nombre de archivo no cumple el patrón permitido",
                    ct);
                continue;
            }

            if (File.Exists(Path.Combine(_options.ProcessedDirectory, file.Name)))
            {
                await MoveToErrorAsync(
                    new PendingFile(file.Name, file.FullName, file.Length, InferFileType(extension)),
                    "Archivo duplicado: ya existe en processed",
                    ct);
                continue;
            }

            pending.Add(new PendingFile(
                file.Name,
                file.FullName,
                file.Length,
                InferFileType(extension)));
        }

        _logger.LogInformation(
            "Found {Count} pending files in {InputDirectory}",
            pending.Count,
            _options.InputDirectory);

        return pending;
    }

    public async Task<IReadOnlyList<TransactionDto>> ParseCsvAsync(PendingFile file, CancellationToken ct = default)
    {
        if (!File.Exists(file.FullPath))
            throw new FileNotFoundException("Archivo no encontrado para parseo CSV", file.FullPath);

        var transactions = new List<TransactionDto>();
        var rowNumber = 0;

        await using var stream = new FileStream(
            file.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var headerLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(headerLine))
            return transactions;

        rowNumber = 1;
        var headerFields = ParseCsvLine(headerLine);
        var headerMap = BuildHeaderIndexMap(headerFields);
        ValidateRequiredHeaders(headerMap);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line == null)
                break;
            rowNumber++;

            var fields = ParseCsvLine(line);
            if (fields == null || fields.All(string.IsNullOrWhiteSpace))
                continue;

            var row = MapRow(headerMap, fields);

            if (TryMapTransaction(row, rowNumber, out var transaction, out var error))
            {
                transactions.Add(transaction!);
            }
            else
            {
                _logger.LogWarning(
                    "CSV row {Row} rejected in {FileName}: {Error}",
                    rowNumber, file.FileName, error);
            }
        }

        _logger.LogInformation(
            "CSV parsed for {FileName}. Valid rows: {Count}",
            file.FileName, transactions.Count);

        return transactions;
    }

    public async Task<IReadOnlyList<TransactionDto>> ParseExcelAsync(PendingFile file, CancellationToken ct = default)
    {
        if (!File.Exists(file.FullPath))
            throw new FileNotFoundException("Archivo no encontrado para parseo Excel", file.FullPath);

        if (Path.GetExtension(file.FullPath).Equals(".xls", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Formato .xls no soportado. Use .xlsx.");

        var sharedStrings = new List<string>();
        await using var fileStream = new FileStream(
            file.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);

        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry != null)
        {
            using var stream = sharedStringsEntry.Open();
            sharedStrings = await ReadSharedStringsAsync(stream, ct);
        }

        var worksheetPath = ResolveFirstWorksheetPath(archive);
        var worksheetEntry = archive.GetEntry(worksheetPath)
            ?? throw new InvalidDataException("No se encontró la hoja principal en el archivo Excel.");

        using var worksheetStream = worksheetEntry.Open();
        var rows = await ReadWorksheetRowsAsync(worksheetStream, sharedStrings, ct);
        if (rows.Count == 0)
            return Array.Empty<TransactionDto>();

        var headerMap = BuildHeaderIndexMap(rows[0]);
        ValidateRequiredHeaders(headerMap);

        var transactions = new List<TransactionDto>();

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var fields = rows[rowIndex];
            if (fields.All(string.IsNullOrWhiteSpace))
                continue;

            var row = MapRow(headerMap, fields);
            var humanRow = rowIndex + 1;

            if (TryMapTransaction(row, humanRow, out var transaction, out var error))
            {
                transactions.Add(transaction!);
            }
            else
            {
                _logger.LogWarning(
                    "Excel row {Row} rejected in {FileName}: {Error}",
                    humanRow, file.FileName, error);
            }
        }

        _logger.LogInformation(
            "Excel parsed for {FileName}. Valid rows: {Count}",
            file.FileName, transactions.Count);

        return transactions;
    }

    public async Task MoveToProcessedAsync(PendingFile file, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureDirectoriesExist();

        if (!File.Exists(file.FullPath))
        {
            _logger.LogWarning("Processed move skipped. File not found: {FilePath}", file.FullPath);
            return;
        }

        var destinationPath = await MoveFileWithRetryAsync(file.FullPath, _options.ProcessedDirectory, file.FileName, ct);

        _logger.LogInformation(
            "File moved to processed: {FileName} -> {Destination}",
            file.FileName, destinationPath);
    }

    public async Task MoveToErrorAsync(PendingFile file, string error, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureDirectoriesExist();

        if (!File.Exists(file.FullPath))
        {
            _logger.LogWarning(
                "Error move skipped. File not found: {FilePath}. Reason: {Error}",
                file.FullPath, error);
            return;
        }

        var destinationPath = await MoveFileWithRetryAsync(file.FullPath, _options.ErrorDirectory, file.FileName, ct);

        var errorTracePath = $"{destinationPath}.error.txt";
        await File.WriteAllTextAsync(errorTracePath, $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}{error}", ct);

        _logger.LogWarning(
            "File moved to error: {FileName} -> {Destination}. Reason: {Error}",
            file.FileName, destinationPath, error);
    }

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(_options.InputDirectory);
        Directory.CreateDirectory(_options.ProcessedDirectory);
        Directory.CreateDirectory(_options.ErrorDirectory);
    }

    private FileType InferFileType(string extension)
        => extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? FileType.Csv
            : FileType.Excel;

    private string BuildDestinationPath(string destinationDirectory, string originalFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var destination = Path.Combine(destinationDirectory, originalFileName);

        if (!File.Exists(destination))
            return destination;

        var suffix = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var counter = 0;
        do
        {
            counter++;
            destination = Path.Combine(destinationDirectory, $"{baseName}_{suffix}_{counter}{extension}");
        } while (File.Exists(destination));

        return destination;
    }

    private async Task<string> MoveFileWithRetryAsync(
        string sourcePath,
        string destinationDirectory,
        string originalFileName,
        CancellationToken ct)
    {
        var attempts = 0;
        while (attempts < MaxMoveRetryAttempts)
        {
            ct.ThrowIfCancellationRequested();
            attempts++;
            var destinationPath = BuildDestinationPath(destinationDirectory, originalFileName);
            try
            {
                await CopyAndDeleteAsync(sourcePath, destinationPath, ct);
                return destinationPath;
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                // Race condition: another process created the same destination between check and move.
                if (attempts >= MaxMoveRetryAttempts)
                    throw;
            }
        }

        throw new IOException($"No se pudo mover el archivo {sourcePath} después de múltiples reintentos.");
    }

    private static async Task CopyAndDeleteAsync(string sourcePath, string destinationPath, CancellationToken ct)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(destination, ct);
        await destination.FlushAsync(ct);
        source.Close();
        File.Delete(sourcePath);
    }

    private static Dictionary<string, int> BuildHeaderIndexMap(IEnumerable<string> headers)
    {
        var map = new Dictionary<string, int>(CaseInsensitiveHeaderComparer);
        var index = 0;
        foreach (var header in headers)
        {
            var normalized = NormalizeHeader(header);
            if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
            {
                map[normalized] = index;
            }
            index++;
        }

        return map;
    }

    private static void ValidateRequiredHeaders(IReadOnlyDictionary<string, int> headerMap)
    {
        var required = new[]
        {
            "externalid", "accountid", "transactiontype", "amount", "currencycode", "valuedate"
        };

        var missing = required.Where(h => !headerMap.ContainsKey(h)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Columnas requeridas faltantes: {string.Join(", ", missing)}");
    }

    private static Dictionary<string, string> MapRow(
        IReadOnlyDictionary<string, int> headerMap,
        IReadOnlyList<string> fields)
    {
        var row = new Dictionary<string, string>(CaseInsensitiveHeaderComparer);
        foreach (var (header, index) in headerMap)
        {
            row[header] = index < fields.Count ? (fields[index] ?? string.Empty).Trim() : string.Empty;
        }

        return row;
    }

    private static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return string.Empty;

        return new string(header
            .Trim()
            .ToLowerInvariant()
            .Where(c => c != '_' && c != '-' && !char.IsWhiteSpace(c))
            .ToArray());
    }
private static string? ReadValue(Dictionary<string, string> row, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var normalized = NormalizeHeader(alias);
            if (row.TryGetValue(normalized, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
            return true;

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result))
            return true;

        var normalized = value.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseDateOnly(string value, out DateOnly date)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        if (DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
            return true;

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var excelSerial))
        {
            var dateTime = DateTime.FromOADate(excelSerial);
            date = DateOnly.FromDateTime(dateTime);
            return true;
        }

        date = default;
        return false;
    }

    private bool TryMapTransaction(
        Dictionary<string, string> row,
        int rowNumber,
        out TransactionDto? transaction,
        out string? error)
    {
        transaction = null;
        error = null;

        var externalId = ReadValue(row, "external_id", "externalid", "id");
        if (string.IsNullOrWhiteSpace(externalId))
        {
            error = "ExternalId requerido";
            return false;
        }

        var accountIdValue = ReadValue(row, "account_id", "accountid");
        if (!Guid.TryParse(accountIdValue, out var accountId))
        {
            error = "AccountId inválido";
            return false;
        }

        var transactionType = ReadValue(row, "transaction_type", "transactiontype", "type");
        if (string.IsNullOrWhiteSpace(transactionType))
        {
            error = "TransactionType requerido";
            return false;
        }

        if (!ValidTransactionTypes.Contains(transactionType))
        {
            error = $"TransactionType inválido: {transactionType}";
            return false;
        }

        var amountValue = ReadValue(row, "amount", "monto");
        if (string.IsNullOrWhiteSpace(amountValue) || !TryParseDecimal(amountValue, out var amount))
        {
            error = "Amount inválido";
            return false;
        }

        var currencyCode = ReadValue(row, "currency_code", "currencycode", "currency", "moneda");
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Length != Iso4217CurrencyCodeLength)
        {
            error = "CurrencyCode inválido";
            return false;
        }

        var valueDateValue = ReadValue(row, "value_date", "valuedate", "date", "fecha");
        if (string.IsNullOrWhiteSpace(valueDateValue) || !TryParseDateOnly(valueDateValue, out var valueDate))
        {
            error = "ValueDate inválido";
            return false;
        }

        DateOnly? bookingDate = null;
        var bookingDateValue = ReadValue(row, "booking_date", "bookingdate");
        if (!string.IsNullOrWhiteSpace(bookingDateValue))
        {
            if (!TryParseDateOnly(bookingDateValue, out var parsedBookingDate))
            {
                error = "BookingDate inválido";
                return false;
            }

            bookingDate = parsedBookingDate;
        }

        decimal? originalAmount = null;
        var originalAmountValue = ReadValue(row, "original_amount", "originalamount");
        if (!string.IsNullOrWhiteSpace(originalAmountValue))
        {
            if (!TryParseDecimal(originalAmountValue, out var parsedOriginalAmount))
            {
                error = "OriginalAmount inválido";
                return false;
            }

            originalAmount = parsedOriginalAmount;
        }

        var originalCurrency = ReadValue(row, "original_currency", "originalcurrency");

        if ((originalAmount.HasValue && string.IsNullOrWhiteSpace(originalCurrency)) ||
            (!originalAmount.HasValue && !string.IsNullOrWhiteSpace(originalCurrency)))
        {
            error = "OriginalAmount y OriginalCurrency deben venir juntos";
            return false;
        }

        var rawData = row
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => (object)kv.Value, CaseInsensitiveHeaderComparer);

        transaction = new TransactionDto
        {
            ExternalId = externalId,
            AccountId = accountId,
            TransactionType = transactionType,
            Amount = amount,
            CurrencyCode = currencyCode.ToUpperInvariant(),
            ValueDate = valueDate,
            BookingDate = bookingDate,
            Description = ReadValue(row, "description", "descripcion", "detail"),
            Category = ReadValue(row, "category", "categoria"),
            CounterpartyName = ReadValue(row, "counterparty_name", "counterpartyname"),
            CounterpartyAccount = ReadValue(row, "counterparty_account", "counterpartyaccount"),
            CounterpartyBank = ReadValue(row, "counterparty_bank", "counterpartybank"),
            OriginalAmount = originalAmount,
            OriginalCurrency = originalCurrency?.ToUpperInvariant(),
            RawData = rawData.Count > 0 ? rawData : null
        };

        _logger.LogDebug("Transaction row {Row} mapped successfully. ExternalId: {ExternalId}", rowNumber, externalId);
        return true;
    }

    private static async Task<List<string>> ReadSharedStringsAsync(Stream stream, CancellationToken ct)
    {
        var sharedStrings = new List<string>();
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreWhitespace = true
        };

        using var reader = XmlReader.Create(stream, settings);
        while (await reader.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element || !string.Equals(reader.LocalName, "si", StringComparison.Ordinal))
                continue;

            using var siReader = reader.ReadSubtree();
            var text = new StringBuilder();
            await siReader.ReadAsync();
            while (await siReader.ReadAsync())
            {
                if (siReader.NodeType == XmlNodeType.Element &&
                    string.Equals(siReader.LocalName, "t", StringComparison.Ordinal))
                {
                    text.Append(await siReader.ReadElementContentAsStringAsync());
                }
            }

            sharedStrings.Add(text.ToString());
        }

        return sharedStrings;
    }

    private static string ResolveFirstWorksheetPath(ZipArchive archive)
    {
        XNamespace mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("workbook.xml no encontrado en Excel.");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("workbook.xml.rels no encontrado en Excel.");

        using var workbookStream = workbookEntry.Open();
        using var relsStream = relsEntry.Open();

        var workbookDoc = XDocument.Load(workbookStream);
        var relsDoc = XDocument.Load(relsStream);

        var firstSheet = workbookDoc.Descendants(mainNs + "sheet").FirstOrDefault()
            ?? throw new InvalidDataException("No hay hojas en el archivo Excel.");
        var relationId = firstSheet.Attribute(relNs + "id")?.Value
            ?? throw new InvalidDataException("La hoja no tiene relación válida.");

        var target = relsDoc.Descendants(pkgRelNs + "Relationship")
            .FirstOrDefault(r => string.Equals(r.Attribute("Id")?.Value, relationId, StringComparison.Ordinal))
            ?.Attribute("Target")?.Value
            ?? throw new InvalidDataException("No se pudo resolver la ruta de la hoja.");

        var normalized = target.Replace('\\', '/');
        if (normalized.StartsWith("/xl/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];
        else if (normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = normalized[1..];

        return normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? normalized : $"xl/{normalized}";
    }

    private static async Task<List<string[]>> ReadWorksheetRowsAsync(
        Stream stream,
        IReadOnlyList<string> sharedStrings,
        CancellationToken ct)
    {
        var rows = new List<string[]>();
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreWhitespace = true
        };

        using var reader = XmlReader.Create(stream, settings);
        while (await reader.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || !string.Equals(reader.LocalName, "row", StringComparison.Ordinal))
                continue;

            var valuesByColumn = new Dictionary<int, string>();

            using var rowReader = reader.ReadSubtree();
            await rowReader.ReadAsync();
            while (await rowReader.ReadAsync())
            {
                if (rowReader.NodeType != XmlNodeType.Element || !string.Equals(rowReader.LocalName, "c", StringComparison.Ordinal))
                    continue;

                var refValue = rowReader.GetAttribute("r");
                var column = GetColumnIndex(refValue);
                valuesByColumn[column] = await ReadCellValueAsync(rowReader, sharedStrings, ct);
            }

            if (valuesByColumn.Count == 0)
            {
                rows.Add(Array.Empty<string>());
                continue;
            }

            var max = valuesByColumn.Keys.Max();
            var row = new string[max + 1];
            for (var i = 0; i <= max; i++)
                row[i] = valuesByColumn.TryGetValue(i, out var value) ? value : string.Empty;

            rows.Add(row);
        }

        return rows;
    }

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
            return 0;

        var letters = new string(cellReference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        if (letters.Length == 0)
            return 0;

        var index = 0;
        foreach (var c in letters)
        {
            index = (index * 26) + (c - 'A' + 1);
        }

        return index - 1;
    }

    private static async Task<string> ReadCellValueAsync(XmlReader cellReader, IReadOnlyList<string> sharedStrings, CancellationToken ct)
    {
        var type = cellReader.GetAttribute("t");
        string? rawValue = null;
        string? inlineStr = null;

        using var subtree = cellReader.ReadSubtree();
        await subtree.ReadAsync();
        while (await subtree.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            if (subtree.NodeType != XmlNodeType.Element)
                continue;

            if (string.Equals(subtree.LocalName, "v", StringComparison.Ordinal))
            {
                rawValue = await subtree.ReadElementContentAsStringAsync();
            }
            else if (string.Equals(subtree.LocalName, "t", StringComparison.Ordinal))
            {
                inlineStr = await subtree.ReadElementContentAsStringAsync();
            }
        }

        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
            return inlineStr ?? string.Empty;

        if (string.Equals(type, "s", StringComparison.Ordinal) &&
            int.TryParse(rawValue, out var sharedStringIndex) &&
            sharedStringIndex >= 0 &&
            sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return rawValue ?? string.Empty;
    }
}

#endregion

#region Exchange Rate Provider

[Obsolete("ExchangeRateProvider es un proveedor mock para desarrollo/testing. No usar en producción.")]
public class ExchangeRateProvider : IExchangeRateProvider
{
    private readonly ILogger<ExchangeRateProvider> _logger;

    public ExchangeRateProvider(ILogger<ExchangeRateProvider> logger)
    {
        _logger = logger;
        _logger.LogWarning("ExchangeRateProvider mock está habilitado. Configurar proveedor real para producción.");
    }

    public string ProviderName => "MockProvider";

    public Task<IEnumerable<ExchangeRateData>> GetLatestRatesAsync(
        string baseCurrency, string[] targetCurrencies, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching rates for {Base}", baseCurrency);
        
        var rates = targetCurrencies.Select(target => 
            new ExchangeRateData(baseCurrency, target, GetMockRate(baseCurrency, target)));

        return Task.FromResult(rates);
    }

    private static decimal GetMockRate(string from, string to) => (from, to) switch
    {
        ("USD", "COP") => 4150.00m,
        ("USD", "EUR") => 0.92m,
        ("USD", "MXN") => 17.15m,
        ("USD", "BRL") => 4.95m,
        ("EUR", "USD") => 1.09m,
        ("COP", "USD") => 0.00024m,
        _ => 1.00m
    };
}

#endregion

#region Reconciliation Engine

public class ReconciliationEngine : IReconciliationEngine
{
    private readonly ILogger<ReconciliationEngine> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public ReconciliationEngine(ILogger<ReconciliationEngine> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public async Task<ReconciliationResult> ReconcileAsync(Guid accountId, DateOnly date, CancellationToken ct = default)
    {
        var accountRef = accountId.ToString("N")[..8];
        _logger.LogInformation("Reconciling account ref {AccountRef} for {Date}", accountRef, date);

        var transactions = await _unitOfWork.Transactions.GetByAccountAndDateRangeAsync(accountId, date, date, ct);
        var dailyBalance = await _unitOfWork.DailyBalances.GetByAccountAndDateAsync(accountId, date, ct);
        var previousBalance = await _unitOfWork.DailyBalances.GetByAccountAndDateAsync(accountId, date.AddDays(-1), ct);

        var postedTransactions = transactions
            .Where(t => t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Reconciled)
            .ToList();

        var duplicateCount = postedTransactions
            .GroupBy(t => t.ExternalId, StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Count() > 1);

        var postedAmount = postedTransactions.Sum(t => t.Amount.Amount);
        var openingBalance = previousBalance?.ClosingBalance ?? dailyBalance?.OpeningBalance ?? 0m;
        var expectedClosing = openingBalance + postedAmount;
        var actualClosing = dailyBalance?.ClosingBalance ?? expectedClosing;
        var discrepancyAmount = actualClosing - expectedClosing;
        var unmatchedCount = duplicateCount + Math.Max(0, transactions.Count - postedTransactions.Count);
        var matchedCount = Math.Max(0, postedTransactions.Count - duplicateCount);
        var hasDiscrepancies = unmatchedCount > 0 || Math.Abs(discrepancyAmount) > 0.01m;

        _logger.LogInformation(
            "Reconciliation account ref {AccountRef} date {Date}: matched={Matched}, unmatched={Unmatched}, discrepancy={Discrepancy}",
            accountRef,
            date,
            matchedCount,
            unmatchedCount,
            discrepancyAmount);

        return new ReconciliationResult(
            MatchedCount: matchedCount,
            UnmatchedCount: unmatchedCount,
            DiscrepancyAmount: discrepancyAmount,
            HasDiscrepancies: hasDiscrepancies
        );
    }
}

#endregion