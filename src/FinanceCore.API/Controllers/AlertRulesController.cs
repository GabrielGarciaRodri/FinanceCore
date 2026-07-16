using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FinanceCore.Application.Alerts;

namespace FinanceCore.API.Controllers;

/// <summary>
/// Administración de reglas de alerta de negocio (SCRUM-45): payout esperado
/// que no llegó, discrepancia sobre umbral y saldo bajo, con entrega por
/// email y/o webhook.
/// </summary>
[ApiController]
[Route("api/alert-rules")]
[Authorize(Policy = "AdminOnly")]
public class AlertRulesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AlertRulesController> _logger;

    public AlertRulesController(
        IMediator mediator,
        ILogger<AlertRulesController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Lista todas las reglas de alerta (habilitadas y deshabilitadas).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AlertRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListAlertRulesQuery(), cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// Crea una regla de alerta.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AlertRuleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAlertRuleCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new ProblemDetails
            {
                Title = "Error creando la regla",
                Detail = result.Error,
                Status = StatusCodes.Status400BadRequest
            });

        _logger.LogInformation(
            "Regla de alerta creada: {Name} [{Type}] (id {Id})",
            result.Value!.Name, result.Value.Type, result.Value.Id);

        return CreatedAtAction(nameof(List), new { }, result.Value);
    }

    /// <summary>
    /// Actualiza una regla de alerta (incluye habilitar/deshabilitar).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AlertRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateAlertRuleCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command with { Id = id }, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new ProblemDetails
            {
                Title = "Error actualizando la regla",
                Detail = result.Error,
                Status = StatusCodes.Status400BadRequest
            });

        return Ok(result.Value);
    }

    /// <summary>
    /// Elimina una regla de alerta.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteAlertRuleCommand(id), cancellationToken);

        if (result.IsFailure)
            return NotFound(new ProblemDetails
            {
                Title = "Regla no encontrada",
                Detail = result.Error,
                Status = StatusCodes.Status404NotFound
            });

        _logger.LogInformation("Regla de alerta eliminada: {Id}", id);
        return NoContent();
    }
}
