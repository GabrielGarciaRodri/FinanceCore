using MediatR;
using Microsoft.AspNetCore.Mvc;
using FinanceCore.Application.Transactions.Commands.IngestTransactions;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.API.Controllers;

/// <summary>
/// Controlador para operaciones con transacciones financieras.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TransactionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        IMediator mediator,
        ILogger<TransactionsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Ingesta un batch de transacciones desde una fuente externa.
    /// </summary>
    /// <param name="request">Datos de las transacciones a ingerir</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado de la ingesta</returns>
    /// <response code="200">Ingesta completada (puede incluir errores parciales)</response>
    /// <response code="400">Request inválido</response>
    /// <response code="500">Error interno del servidor</response>
    [HttpPost("ingest")]
    [ProducesResponseType(typeof(IngestTransactionsResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> IngestTransactions(
        [FromBody] IngestTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Recibida solicitud de ingesta de {Count} transacciones desde {Source}",
            request.Transactions?.Count ?? 0,
            request.Source);

        var command = new IngestTransactionsCommand
        {
            Source = request.Source,
            SourceType = request.SourceType,
            Transactions = request.Transactions?.Select(t => new TransactionDto
            {
                ExternalId = t.ExternalId,
                AccountId = t.AccountId,
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                CurrencyCode = t.CurrencyCode,
                ValueDate = t.ValueDate,
                BookingDate = t.BookingDate,
                Description = t.Description,
                Category = t.Category,
                CounterpartyName = t.CounterpartyName,
                CounterpartyAccount = t.CounterpartyAccount,
                CounterpartyBank = t.CounterpartyBank,
                OriginalAmount = t.OriginalAmount,
                OriginalCurrency = t.OriginalCurrency,
                RawData = t.RawData
            }).ToList() ?? new List<TransactionDto>(),
            FailOnFirstError = request.FailOnFirstError,
            Metadata = request.Metadata
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Error en ingesta",
                Detail = result.Error,
                Status = StatusCodes.Status400BadRequest
            });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Obtiene una transacción por su ID.
    /// </summary>
    /// <param name="id">ID de la transacción</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Detalle de la transacción</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TransactionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        // TODO: Implementar query con MediatR
        await Task.CompletedTask;
        return NotFound();
    }

    /// <summary>
    /// Busca transacciones con criterios avanzados.
    /// </summary>
    /// <param name="request">Criterios de búsqueda</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Lista paginada de transacciones</returns>
    [HttpGet("search")]
    [ProducesResponseType(typeof(PagedResult<TransactionListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] SearchTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        // TODO: Implementar query con MediatR
        await Task.CompletedTask;
        return Ok(new { Items = new List<object>(), TotalCount = 0 });
    }

    /// <summary>
    /// Obtiene el resumen de transacciones para una cuenta.
    /// </summary>
    /// <param name="accountId">ID de la cuenta</param>
    /// <param name="startDate">Fecha inicio</param>
    /// <param name="endDate">Fecha fin</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resumen de transacciones</returns>
    [HttpGet("accounts/{accountId:guid}/summary")]
    [ProducesResponseType(typeof(TransactionSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccountSummary(
        Guid accountId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken)
    {
        // TODO: Implementar query con MediatR
        await Task.CompletedTask;
        return NotFound();
    }

    /// <summary>
    /// Obtiene transacciones pendientes de conciliación.
    /// </summary>
    /// <param name="accountId">ID de la cuenta (opcional)</param>
    /// <param name="limit">Límite de resultados</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Lista de transacciones pendientes</returns>
    [HttpGet("pending-reconciliation")]
    [ProducesResponseType(typeof(IEnumerable<TransactionListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingReconciliation(
        [FromQuery] Guid? accountId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implementar query con MediatR
        await Task.CompletedTask;
        return Ok(new List<object>());
    }
}

#region Request/Response DTOs

/// <summary>
/// Request para ingesta de transacciones.
/// </summary>
public class IngestTransactionsRequest
{
    /// <summary>
    /// Identificador de la fuente de datos.
    /// </summary>
    public string Source { get; set; } = null!;
    
    /// <summary>
    /// Tipo de fuente.
    /// </summary>
    public Domain.Enums.SourceType SourceType { get; set; }
    
    /// <summary>
    /// Lista de transacciones a ingerir.
    /// </summary>
    public List<TransactionInput>? Transactions { get; set; }
    
    /// <summary>
    /// Si es true, falla todo el batch si una transacción falla.
    /// </summary>
    public bool FailOnFirstError { get; set; }
    
    /// <summary>
    /// Metadata adicional.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Datos de una transacción para ingesta.
/// </summary>
public class TransactionInput
{
    public string ExternalId { get; set; } = null!;
    public Guid AccountId { get; set; }
    public string TransactionType { get; set; } = null!;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public DateOnly ValueDate { get; set; }
    public DateOnly? BookingDate { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? CounterpartyName { get; set; }
    public string? CounterpartyAccount { get; set; }
    public string? CounterpartyBank { get; set; }
    public decimal? OriginalAmount { get; set; }
    public string? OriginalCurrency { get; set; }
    public Dictionary<string, object>? RawData { get; set; }
}

/// <summary>
/// Request para búsqueda de transacciones.
/// </summary>
public class SearchTransactionsRequest
{
    public Guid? AccountId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public string? Category { get; set; }
    public string? SearchText { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
}

/// <summary>
/// Detalle completo de una transacción.
/// </summary>
public class TransactionDetailResponse
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = null!;
    public Guid AccountId { get; set; }
    public string AccountNumber { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Status { get; set; } = null!;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public DateOnly ValueDate { get; set; }
    public DateOnly BookingDate { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public CounterpartyResponse? Counterparty { get; set; }
    public CurrencyConversionResponse? CurrencyConversion { get; set; }
    public ReconciliationInfoResponse? Reconciliation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public class CounterpartyResponse
{
    public string? Name { get; set; }
    public string? AccountNumber { get; set; }
    public string? BankName { get; set; }
}

public class CurrencyConversionResponse
{
    public decimal OriginalAmount { get; set; }
    public string OriginalCurrency { get; set; } = null!;
    public decimal ExchangeRate { get; set; }
}

public class ReconciliationInfoResponse
{
    public Guid ReconciliationId { get; set; }
    public DateTimeOffset ReconciledAt { get; set; }
}

/// <summary>
/// Item de lista de transacciones.
/// </summary>
public class TransactionListItem
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = null!;
    public string AccountNumber { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Status { get; set; } = null!;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public DateOnly ValueDate { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsReconciled { get; set; }
}

#endregion
