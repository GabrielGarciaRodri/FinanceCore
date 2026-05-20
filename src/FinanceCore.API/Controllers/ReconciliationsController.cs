using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FinanceCore.Application.Reconciliations.Queries;
using FinanceCore.Domain.Enums;
using FinanceCore.Infrastructure.BackgroundJobs.Configuration;

namespace FinanceCore.API.Controllers;

/// <summary>
/// Controlador para consulta y disparo de conciliaciones.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class ReconciliationsController : ControllerBase
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
