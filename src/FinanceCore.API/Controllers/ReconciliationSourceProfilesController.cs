using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FinanceCore.Application.Reconciliations.Commands.SourceProfiles;

namespace FinanceCore.API.Controllers;

/// <summary>
/// Administración de perfiles de conciliación por fuente/pasarela (matching N:1).
/// Un perfil define cómo reconocer los payouts de una pasarela en el extracto,
/// cómo identificar sus ventas internas y qué comisión esperar.
/// </summary>
[ApiController]
[Route("api/reconciliation-source-profiles")]
[Authorize(Policy = "AdminOnly")]
public class ReconciliationSourceProfilesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<ReconciliationSourceProfilesController> _logger;

    public ReconciliationSourceProfilesController(
        IMediator mediator,
        ILogger<ReconciliationSourceProfilesController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Lista todos los perfiles de fuente (activos e inactivos).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SourceProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListSourceProfilesQuery(), cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// Crea un perfil de fuente.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SourceProfileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSourceProfileCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new ProblemDetails
            {
                Title = "Error creando el perfil",
                Detail = result.Error,
                Status = StatusCodes.Status400BadRequest
            });

        _logger.LogInformation(
            "Perfil de fuente creado: {SourceKey} (id {Id})",
            result.Value!.SourceKey, result.Value.Id);

        return CreatedAtAction(nameof(List), new { }, result.Value);
    }

    /// <summary>
    /// Actualiza un perfil de fuente (incluye activar/desactivar).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SourceProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateSourceProfileCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command with { Id = id }, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new ProblemDetails
            {
                Title = "Error actualizando el perfil",
                Detail = result.Error,
                Status = StatusCodes.Status400BadRequest
            });

        return Ok(result.Value);
    }
}
