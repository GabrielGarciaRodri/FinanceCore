using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FinanceCore.Application.Accounts.Queries;

namespace FinanceCore.API.Controllers;

/// <summary>
/// Endpoints para listar cuentas financieras. Pensado para poblar selectores
/// en el web UI (filtros de transacciones, reconciliaciones, upload).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class AccountsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccountsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Lista cuentas activas (o todas si includeInactive=true).
    /// Cacheable por 60 segundos.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AccountListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetAccountsQuery { IncludeInactive = includeInactive },
            cancellationToken);

        return Ok(result.Value);
    }
}
