using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Exceptions;
using FinanceCore.Domain.Repositories;
using FinanceCore.Domain.ValueObjects;
using FinanceCore.Application.Common.Models;

namespace FinanceCore.Application.Transactions.Commands.IngestTransactions;

/// <summary>
/// Comando para ingerir transacciones desde una fuente externa.
/// Soporta múltiples transacciones en batch para procesamiento eficiente.
/// </summary>
public record IngestTransactionsCommand : IRequest<Result<IngestTransactionsResult>>
{
    /// <summary>
    /// Identificador del batch de ingesta.
    /// </summary>
    public Guid BatchId { get; init; } = Guid.NewGuid();
    
    /// <summary>
    /// Fuente de los datos.
    /// </summary>
    public required string Source { get; init; }
    
    /// <summary>
    /// Tipo de fuente.
    /// </summary>
    public SourceType SourceType { get; init; }
    
    /// <summary>
    /// Transacciones a ingerir.
    /// </summary>
    public required IReadOnlyList<TransactionDto> Transactions { get; init; }
    
    /// <summary>
    /// Si es true, falla todo el batch si una transacción falla.
    /// Si es false, continúa con las demás.
    /// </summary>
    public bool FailOnFirstError { get; init; } = false;
    
    /// <summary>
    /// Metadata adicional del proceso de ingesta.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// DTO para transacciones a ingerir.
/// </summary>
public record TransactionDto
{
    public required string ExternalId { get; init; }
    public required Guid AccountId { get; init; }
    public required string TransactionType { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public required DateOnly ValueDate { get; init; }
    public DateOnly? BookingDate { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? CounterpartyName { get; init; }
    public string? CounterpartyAccount { get; init; }
    public string? CounterpartyBank { get; init; }
    public decimal? OriginalAmount { get; init; }
    public string? OriginalCurrency { get; init; }
    public Dictionary<string, object>? RawData { get; init; }
}

/// <summary>
/// Resultado de la ingesta de transacciones.
/// </summary>
public record IngestTransactionsResult
{
    public Guid BatchId { get; init; }
    public int TotalReceived { get; init; }
    public int Processed { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public int Duplicates { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<TransactionResult> Results { get; init; } = Array.Empty<TransactionResult>();
}

public record TransactionResult
{
    public string ExternalId { get; init; } = string.Empty;
    public Guid? TransactionId { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsDuplicate { get; init; }
}

/// <summary>
/// Validador del comando de ingesta.
/// </summary>
public class IngestTransactionsCommandValidator : AbstractValidator<IngestTransactionsCommand>
{
    public IngestTransactionsCommandValidator()
    {
        RuleFor(x => x.Source)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Transactions)
            .NotEmpty()
            .WithMessage("Debe incluir al menos una transacción.");

        RuleForEach(x => x.Transactions)
            .SetValidator(new TransactionDtoValidator());
    }
}

public class TransactionDtoValidator : AbstractValidator<TransactionDto>
{
    private static readonly HashSet<string> ValidTransactionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "debit", "credit", "transfer_out", "transfer_in", "fee", "interest", "adjustment"
    };

    public TransactionDtoValidator()
    {
        RuleFor(x => x.ExternalId)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.AccountId)
            .NotEmpty();

        RuleFor(x => x.TransactionType)
            .NotEmpty()
            .Must(t => ValidTransactionTypes.Contains(t))
            .WithMessage("Tipo de transacción inválido. Valores válidos: " + string.Join(", ", ValidTransactionTypes));

        RuleFor(x => x.Amount)
            .NotEqual(0)
            .WithMessage("El monto no puede ser cero.");

        RuleFor(x => x.CurrencyCode)
            .NotEmpty()
            .Length(3)
            .WithMessage("El código de moneda debe tener 3 caracteres (ISO 4217).");

        RuleFor(x => x.ValueDate)
            .NotEmpty()
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.Today.AddDays(5)))
            .WithMessage("La fecha valor no puede ser mayor a 5 días en el futuro.");

        // Validar conversión de moneda completa o nula
        RuleFor(x => x)
            .Must(x => 
                (x.OriginalAmount == null && x.OriginalCurrency == null) ||
                (x.OriginalAmount != null && x.OriginalCurrency != null))
            .WithMessage("Si especifica monto original, debe incluir también la moneda original.");
    }
}

/// <summary>
/// Handler para el comando de ingesta de transacciones.
/// Implementa idempotencia y procesamiento robusto.
/// </summary>
public class IngestTransactionsCommandHandler 
    : IRequestHandler<IngestTransactionsCommand, Result<IngestTransactionsResult>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IngestTransactionsCommandHandler> _logger;

    public IngestTransactionsCommandHandler(
        IUnitOfWork unitOfWork,
        ILogger<IngestTransactionsCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<IngestTransactionsResult>> Handle(
        IngestTransactionsCommand request,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;
        var results = new List<TransactionResult>();
        var succeeded = 0;
        var failed = 0;
        var duplicates = 0;

        _logger.LogInformation(
            "Iniciando ingesta de {Count} transacciones. BatchId: {BatchId}, Source: {Source}",
            request.Transactions.Count,
            request.BatchId,
            request.Source);

        try
        {
            // Procesar en una transacción de base de datos
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            foreach (var dto in request.Transactions)
            {
                try
                {
                    var result = await ProcessTransactionAsync(dto, request.Source, cancellationToken);
                    results.Add(result);

                    if (result.Success)
                    {
                        if (result.IsDuplicate)
                            duplicates++;
                        else
                            succeeded++;
                    }
                    else
                    {
                        failed++;
                        
                        if (request.FailOnFirstError)
                        {
                            _logger.LogWarning(
                                "Fallo en transacción {ExternalId}, abortando batch por FailOnFirstError",
                                dto.ExternalId);
                            
                            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                            
                            return Result<IngestTransactionsResult>.Failure(
                                $"Error procesando transacción {dto.ExternalId}: {result.ErrorMessage}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, 
                        "Error inesperado procesando transacción {ExternalId}", 
                        dto.ExternalId);

                    failed++;
                    results.Add(new TransactionResult
                    {
                        ExternalId = dto.ExternalId,
                        Success = false,
                        ErrorMessage = ex.Message
                    });

                    if (request.FailOnFirstError)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        return Result<IngestTransactionsResult>.Failure(ex.Message);
                    }
                }
            }

            // Guardar cambios y confirmar transacción
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var duration = DateTimeOffset.UtcNow - startTime;

            _logger.LogInformation(
                "Ingesta completada. BatchId: {BatchId}, Succeeded: {Succeeded}, " +
                "Failed: {Failed}, Duplicates: {Duplicates}, Duration: {Duration}ms",
                request.BatchId, succeeded, failed, duplicates, duration.TotalMilliseconds);

            return Result<IngestTransactionsResult>.Success(new IngestTransactionsResult
            {
                BatchId = request.BatchId,
                TotalReceived = request.Transactions.Count,
                Processed = request.Transactions.Count,
                Succeeded = succeeded,
                Failed = failed,
                Duplicates = duplicates,
                Duration = duration,
                Results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fatal durante ingesta. BatchId: {BatchId}", request.BatchId);
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result<IngestTransactionsResult>.Failure($"Error durante ingesta: {ex.Message}");
        }
    }

    private async Task<TransactionResult> ProcessTransactionAsync(
        TransactionDto dto,
        string source,
        CancellationToken cancellationToken)
    {
        // 1. Verificar idempotencia - ¿Ya existe esta transacción?
        var existing = await _unitOfWork.Transactions.GetByExternalIdAsync(
            dto.ExternalId, source, cancellationToken);

        if (existing != null)
        {
            _logger.LogDebug(
                "Transacción duplicada detectada: {ExternalId}, Id existente: {Id}",
                dto.ExternalId, existing.Id);

            return new TransactionResult
            {
                ExternalId = dto.ExternalId,
                TransactionId = existing.Id,
                Success = true,
                IsDuplicate = true
            };
        }

        // 2. Verificar que la cuenta existe
        var account = await _unitOfWork.Accounts.GetByIdAsync(dto.AccountId, cancellationToken);
        if (account == null)
        {
            return new TransactionResult
            {
                ExternalId = dto.ExternalId,
                Success = false,
                ErrorMessage = $"Cuenta no encontrada: {dto.AccountId}"
            };
        }

        // 3. Crear la transacción según su tipo
        var transaction = CreateTransaction(dto, source, account);

        // 4. Aplicar conversión de moneda si es necesario
        if (dto.OriginalAmount.HasValue && dto.OriginalCurrency != null)
        {
            await ApplyCurrencyConversionAsync(transaction, dto, cancellationToken);
        }

        // 5. Categorizar si viene categoría
        if (!string.IsNullOrWhiteSpace(dto.Category))
        {
            transaction.Categorize(dto.Category);
        }

        // 6. Establecer contraparte si viene
        if (!string.IsNullOrWhiteSpace(dto.CounterpartyName))
        {
            transaction.SetCounterparty(new CounterpartyInfo
            {
                Name = dto.CounterpartyName,
                AccountNumber = dto.CounterpartyAccount,
                BankName = dto.CounterpartyBank
            });
        }

        // 7. Agregar al repositorio
        _unitOfWork.Transactions.Add(transaction);

        return new TransactionResult
        {
            ExternalId = dto.ExternalId,
            TransactionId = transaction.Id,
            Success = true,
            IsDuplicate = false
        };
    }

    private Transaction CreateTransaction(TransactionDto dto, string source, Account account)
    {
        var type = ParseTransactionType(dto.TransactionType);
        var bookingDate = dto.BookingDate ?? dto.ValueDate;

        return type switch
        {
            TransactionType.Debit => Transaction.CreateDebit(
                dto.ExternalId, source, dto.AccountId,
                dto.Amount, dto.CurrencyCode, dto.ValueDate, bookingDate,
                dto.Description),
                
            TransactionType.Credit => Transaction.CreateCredit(
                dto.ExternalId, source, dto.AccountId,
                dto.Amount, dto.CurrencyCode, dto.ValueDate, bookingDate,
                dto.Description),
                
            TransactionType.TransferOut => Transaction.CreateTransfer(
                dto.ExternalId, source, dto.AccountId,
                dto.Amount, dto.CurrencyCode, dto.ValueDate, true,
                new CounterpartyInfo { Name = dto.CounterpartyName },
                dto.Description),
                
            TransactionType.TransferIn => Transaction.CreateTransfer(
                dto.ExternalId, source, dto.AccountId,
                dto.Amount, dto.CurrencyCode, dto.ValueDate, false,
                new CounterpartyInfo { Name = dto.CounterpartyName },
                dto.Description),
                
            TransactionType.Fee => Transaction.CreateFee(
                dto.ExternalId, source, dto.AccountId,
                dto.Amount, dto.CurrencyCode, dto.ValueDate,
                dto.Description ?? "Fee"),
                
            _ => throw new DomainException($"Tipo de transacción no soportado: {type}")
        };
    }

    private async Task ApplyCurrencyConversionAsync(
        Transaction transaction,
        TransactionDto dto,
        CancellationToken cancellationToken)
    {
        if (!dto.OriginalAmount.HasValue || dto.OriginalCurrency == null)
            return;

        // Obtener tipo de cambio
        var rate = await _unitOfWork.ExchangeRates.GetRateAsync(
            dto.OriginalCurrency,
            dto.CurrencyCode,
            dto.ValueDate,
            cancellationToken);

        if (rate == null)
        {
            _logger.LogWarning(
                "Tipo de cambio no encontrado para {From}->{To} en {Date}. Calculando implícito.",
                dto.OriginalCurrency, dto.CurrencyCode, dto.ValueDate);

            // Calcular tasa implícita desde los montos
            var implicitRate = Math.Abs(dto.Amount / dto.OriginalAmount.Value);
            
            var originalMoney = Money.Create(dto.OriginalAmount.Value, dto.OriginalCurrency);
            var convertedMoney = Money.Create(dto.Amount, dto.CurrencyCode);
            
            transaction.ApplyCurrencyConversion(convertedMoney, implicitRate);
        }
        else
        {
            var originalMoney = Money.Create(dto.OriginalAmount.Value, dto.OriginalCurrency);
            var convertedMoney = Money.Create(dto.Amount, dto.CurrencyCode);
            
            transaction.ApplyCurrencyConversion(convertedMoney, rate.Rate, rate.Id);
        }
    }

    private static TransactionType ParseTransactionType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "debit" => TransactionType.Debit,
            "credit" => TransactionType.Credit,
            "transfer_out" => TransactionType.TransferOut,
            "transfer_in" => TransactionType.TransferIn,
            "fee" => TransactionType.Fee,
            "interest" => TransactionType.Interest,
            "adjustment" => TransactionType.Adjustment,
            _ => throw new DomainException($"Tipo de transacción desconocido: {type}")
        };
    }
}
