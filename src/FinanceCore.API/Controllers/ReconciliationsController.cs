using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FinanceCore.Application.Reconciliations.Commands;
using FinanceCore.Application.Reconciliations.Queries;
using FinanceCore.Domain.Enums;
using FinanceCore.Infrastructure.BackgroundJobs.Configuration;
using FinanceCore.Infrastructure.BackgroundJobs.Jobs;
using FinanceCore.Infrastructure.Exports;
using FinanceCore.Infrastructure.Reconciliations;

namespace FinanceCore.API.Controllers;

/// <summary>
/// Controlador para consulta y disparo de conciliaciones.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public partial class ReconciliationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<ReconciliationsController> _logger;

    public ReconciliationsController(IMediator mediator, ILogger<ReconciliationsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene la conciliación de una cuenta para una fecha específica.
    /// </summary>
    [HttpGet("accounts/{accountId:guid}/date/{date}")]
    [ProducesResponseType(typeof(ReconciliationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByAccountAndDate(
        Guid accountId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetReconciliationByAccountAndDateQuery(accountId, date),
            cancellationToken);

        if (result.IsFailure)
            return NotFound(new ProblemDetails
            {
                Title = "Conciliación no encontrada",
                Detail = result.Error,
                Status = StatusCodes.Status404NotFound
            });

        return Ok(result.Value);
    }

    /// <summary>
    /// Obtiene una conciliación por su ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ReconciliationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetReconciliationByIdQuery(id),
            cancellationToken);

        if (result.IsFailure)
            return NotFound(new ProblemDetails
            {
                Title = "Conciliación no encontrada",
                Detail = result.Error,
                Status = StatusCodes.Status404NotFound
            });

        return Ok(result.Value);
    }

    /// <summary>
    /// Busca conciliaciones con filtros opcionales.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReconciliationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] Guid? accountId,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] ReconciliationStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new SearchReconciliationsQuery
            {
                AccountId = accountId,
                StartDate = startDate,
                EndDate = endDate,
                Status = status,
                Page = page,
                PageSize = pageSize
            },
            cancellationToken);

        return Ok(result.Value);
    }

    /// <summary>
    /// Encola una conciliación bajo demanda para una cuenta y fecha.
    /// </summary>
    [HttpPost("accounts/{accountId:guid}/date/{date}/run")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(EnqueueReconciliationResponse), StatusCodes.Status202Accepted)]
    public IActionResult RunReconciliation(Guid accountId, DateOnly date)
    {
        var jobId = HangfireJobsConfiguration.ScheduleReconciliation(accountId, date);

        _logger.LogInformation(
            "Conciliación manual encolada. Cuenta={AccountId} Fecha={Date} JobId={JobId}",
            accountId, date, jobId);

        return Accepted(new EnqueueReconciliationResponse(jobId, accountId, date));
    }
}

public record EnqueueReconciliationResponse(string JobId, Guid AccountId, DateOnly Date);

#region Manual reconciliation extension

public partial class ReconciliationsController
{
    /// <summary>
    /// Lista discrepancias pendientes de resolución.
    /// </summary>
    [HttpGet("discrepancies/pending")]
    [ProducesResponseType(typeof(IReadOnlyList<PendingDiscrepancyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingDiscrepancies(
        [FromQuery] Guid? accountId,
        [FromQuery] DiscrepancyType? type,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetPendingDiscrepanciesQuery { AccountId = accountId, Type = type, Limit = limit },
            cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// Marca una discrepancia como resuelta.
    /// </summary>
    [HttpPost("{reconciliationId:guid}/discrepancies/{discrepancyId:guid}/resolve")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResolveDiscrepancy(
        Guid reconciliationId,
        Guid discrepancyId,
        [FromBody] ResolveDiscrepancyRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ResolveDiscrepancyCommand
        {
            ReconciliationId = reconciliationId,
            DiscrepancyId = discrepancyId,
            Resolution = request.Resolution,
            ResolvedBy = request.ResolvedBy,
            Notes = request.Notes
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            var status = result.Error?.Contains("no encontrad", StringComparison.OrdinalIgnoreCase) == true
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo resolver la discrepancia",
                Detail = result.Error,
                Status = status
            });
        }

        return NoContent();
    }

    /// <summary>
    /// Aprueba una reconciliación en estado terminal.
    /// </summary>
    [HttpPost("{reconciliationId:guid}/approve")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Approve(
        Guid reconciliationId,
        [FromBody] ApproveReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ApproveReconciliationCommand
        {
            ReconciliationId = reconciliationId,
            ApprovedBy = request.ApprovedBy,
            ResolutionNotes = request.ResolutionNotes
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            var status = result.Error?.Contains("no encontrad", StringComparison.OrdinalIgnoreCase) == true
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo aprobar la reconciliación",
                Detail = result.Error,
                Status = status
            });
        }

        return NoContent();
    }

    /// <summary>
    /// Sube un extracto bancario en CSV y dispara reconciliación statement-based.
    /// Formato esperado: ExternalReference,Amount,CurrencyCode,ValueDate[,Description]
    /// </summary>
    [HttpPost("accounts/{accountId:guid}/date/{date}/statement")]
    [Authorize(Policy = "AdminOnly")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20_000_000)] // 20MB
    [ProducesResponseType(typeof(StatementUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadStatement(
        Guid accountId,
        DateOnly date,
        IFormFile file,
        [FromServices] IReconciliationEngine engine,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new ProblemDetails
            {
                Title = "Archivo requerido",
                Detail = "Debe adjuntar un archivo CSV no vacío.",
                Status = StatusCodes.Status400BadRequest
            });

        await using var stream = file.OpenReadStream();
        var parse = await StatementCsvParser.ParseAsync(stream, cancellationToken);

        if (parse.Errors.Count > 0)
        {
            return BadRequest(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["Statement"] = parse.Errors.ToArray() })
            {
                Title = "Statement CSV inválido",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await engine.ReconcileWithStatementAsync(accountId, date, parse.Lines, cancellationToken);

        return Ok(new StatementUploadResponse(
            ReconciliationId: result.ReconciliationId,
            LinesParsed: parse.Lines.Count,
            Matched: result.MatchedCount,
            UnmatchedInternal: result.UnmatchedInternal,
            UnmatchedExternal: result.UnmatchedExternal,
            DiscrepancyAmount: result.DiscrepancyAmount,
            DiscrepancyCount: result.DiscrepancyCount,
            Status: result.Status.ToString()));
    }
}

public record ResolveDiscrepancyRequest(ResolutionType Resolution, string ResolvedBy, string? Notes);
public record ApproveReconciliationRequest(string ApprovedBy, string? ResolutionNotes);
public partial class ReconciliationsController
{
    /// <summary>
    /// Exporta las discrepancias de una reconciliación a CSV.
    /// </summary>
    [HttpGet("{reconciliationId:guid}/discrepancies.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportDiscrepanciesCsv(
        Guid reconciliationId,
        [FromServices] IReconciliationExportService exportService,
        CancellationToken cancellationToken)
    {
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=discrepancies-{reconciliationId:N}.csv";

        await exportService.WriteDiscrepanciesCsvAsync(reconciliationId, Response.Body, cancellationToken);
        return new EmptyResult();
    }

    /// <summary>
    /// Reporte agregado de reconciliaciones por cuenta en un rango de fechas.
    /// </summary>
    [HttpGet("accounts/{accountId:guid}/range")]
    [ProducesResponseType(typeof(ReconciliationRangeReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRangeReport(
        Guid accountId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] IReconciliationExportService exportService,
        CancellationToken cancellationToken)
    {
        var report = await exportService.BuildRangeReportAsync(accountId, from, to, cancellationToken);
        if (report is null)
            return NotFound(new ProblemDetails
            {
                Title = "Sin reconciliaciones en el rango",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(report);
    }
}

public record StatementUploadResponse(
    Guid ReconciliationId,
    int LinesParsed,
    int Matched,
    int UnmatchedInternal,
    int UnmatchedExternal,
    decimal DiscrepancyAmount,
    int DiscrepancyCount,
    string Status);

#endregion
