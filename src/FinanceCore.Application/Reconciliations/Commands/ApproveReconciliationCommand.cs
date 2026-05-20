using MediatR;
using Microsoft.Extensions.Logging;
using FinanceCore.Application.Common.Behaviors;
using FinanceCore.Application.Common.Models;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.Application.Reconciliations.Commands;

/// <summary>
/// Marca una reconciliación terminal como aprobada por un usuario humano.
/// Aplicable cuando hay discrepancias revisadas y aceptadas.
/// </summary>
public record ApproveReconciliationCommand : IRequest<Result>, ITransactionalRequest
{
    public required Guid ReconciliationId { get; init; }
    public required string ApprovedBy { get; init; }
    public string? ResolutionNotes { get; init; }
}

public class ApproveReconciliationCommandHandler : IRequestHandler<ApproveReconciliationCommand, Result>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ApproveReconciliationCommandHandler> _logger;

    public ApproveReconciliationCommandHandler(
        IUnitOfWork unitOfWork,
        ILogger<ApproveReconciliationCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ApproveReconciliationCommand request, CancellationToken cancellationToken)
    {
        var reconciliation = await _unitOfWork.Reconciliations.GetByIdAsync(request.ReconciliationId, cancellationToken);
        if (reconciliation == null)
            return Result.Failure($"Reconciliación no encontrada: {request.ReconciliationId}");

        try
        {
            reconciliation.Approve(request.ApprovedBy, request.ResolutionNotes);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }

        _logger.LogInformation(
            "Reconciliation {ReconciliationId} approved by {ApprovedBy}",
            request.ReconciliationId, request.ApprovedBy);

        return Result.Success();
    }
}
