using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FinanceCore.Application.Dashboard.Queries;

namespace FinanceCore.API.Controllers;

/// <summary>
/// Endpoint composite que alimenta el home del web UI.
/// Devuelve balances por moneda, time series de actividad, reconciliaciones
/// recientes y quick stats en un único round-trip.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Snapshot del dashboard. Cacheable por 30 segundos (ver CachingBehavior).
    /// </summary>
    /// <param name="activityDays">Días hacia atrás para la time series (1–365). Default 30.</param>
    /// <param name="recentReconciliations">Cantidad de reconciliaciones recientes (1–50). Default 5.</param>
    [HttpGet]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] int activityDays = 30,
        [FromQuery] int recentReconciliations = 5,
        CancellationToken cancellationToken = default)
    {
        var query = new GetDashboardQuery
        {
            ActivityDays = activityDays,
            RecentReconciliationsLimit = recentReconciliations
        };

        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result.Value);
    }
}
