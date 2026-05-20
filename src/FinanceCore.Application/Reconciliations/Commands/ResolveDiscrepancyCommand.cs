using MediatR;
using Microsoft.Extensions.Logging;
using FinanceCore.Application.Common.Behaviors;
using FinanceCore.Application.Common.Models;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.Application.Reconciliations.Commands;

/// <summary>
/// Marca una discrepancia como resuelta con el tipo de resolución indicado.
/// Resolution = Pending o UnderInvestigation no la cierra (queda activa pero anotada).
/// </summary>
public record ResolveDiscrepancyCommand : IRequest<Result>, ITransactionalRequest
{
    public required Guid ReconciliationId { get; init; }
    public required Guid DiscrepancyId { get; init; }
    public required ResolutionType Resolution { get; init; }
    public required string ResolvedBy { get; init; }
    public string? Notes { get; init; }
}

public class ResolveDiscrepancyCommandHandler : IRequestHandler<ResolveDiscrepancyCommand, Result>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ResolveDiscrepancyCommandHandler> _logger;

    public ResolveDiscrepancyCommandHandler(IUnitOfWork unitOfWork, ILogger<ResolveDiscrepancyCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ResolveDiscrepancyCommand request, CancellationToken cancellationToken)
    {
        var reconciliation = await _unitOfWork.Reconciliations.GetByIdAsync(request.ReconciliationId, cancellationToken);
        if (reconciliation == null)
            return Result.Failure($"Reconciliación no encontrada: {request.ReconciliationId}");

        var discrepancy = reconciliation.Discrepancies.FirstOrDefault(d => d.Id == request.DiscrepancyId);
        if (discrepancy == null)
            return Result.Failure($"Discrepancia {request.DiscrepancyId} no pertenece a la reconciliación {request.ReconciliationId}");

        if (discrepancy.IsResolved)
            return Result.Failure("La discrepancia ya está resuelta.");

        discrepancy.Resolve(request.Resolution, request.ResolvedBy, request.Notes);

        _logger.LogInformation(
            "Discrepancy {DiscrepancyId} resolved as {Resolution} by {ResolvedBy}",
            request.DiscrepancyId, request.Resolution, request.ResolvedBy);

        return Result.Success();
    }
}
