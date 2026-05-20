using MediatR;
using FinanceCore.Application.Common.Models;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.Application.Reconciliations.Queries;

/// <summary>
/// Lista discrepancias pendientes de resolución para el workflow de finanzas.
/// Filtros opcionales por cuenta y por tipo de discrepancia.
/// </summary>
public record GetPendingDiscrepanciesQuery : IRequest<Result<IReadOnlyList<PendingDiscrepancyDto>>>
{
    public Guid? AccountId { get; init; }
    public DiscrepancyType? Type { get; init; }
    public int Limit { get; init; } = 200;
}

public record PendingDiscrepancyDto
{
    public Guid Id { get; init; }
    public Guid ReconciliationId { get; init; }
    public Guid AccountId { get; init; }
    public DateOnly ReconciliationDate { get; init; }
    public string DiscrepancyType { get; init; } = null!;
    public Guid? InternalTransactionId { get; init; }
    public string? ExternalReference { get; init; }
    public decimal? InternalAmount { get; init; }
    public decimal? ExternalAmount { get; init; }
    public decimal? DifferenceAmount { get; init; }
    public DateOnly? InternalDate { get; init; }
    public DateOnly? ExternalDate { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public class GetPendingDiscrepanciesQueryHandler
    : IRequestHandler<GetPendingDiscrepanciesQuery, Result<IReadOnlyList<PendingDiscrepancyDto>>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPendingDiscrepanciesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<IReadOnlyList<PendingDiscrepancyDto>>> Handle(
        GetPendingDiscrepanciesQuery request,
        CancellationToken cancellationToken)
    {
        // El repositorio actual no tiene un Search global de discrepancias —
        // hacemos un join contra la tabla de reconciliaciones traídas por estado
        // CompletedWithDiscrepancies en una ventana razonable. Para volumen grande
        // habría que crear una query dedicada en IReconciliationRepository.
        var recent = await _unitOfWork.Reconciliations.SearchAsync(
            status: ReconciliationStatus.CompletedWithDiscrepancies,
            accountId: request.AccountId,
            page: 1,
            pageSize: 500,
            cancellationToken: cancellationToken);

        var pending = recent
            .SelectMany(r => r.Discrepancies
                .Where(d => !d.IsResolved)
                .Where(d => !request.Type.HasValue || d.DiscrepancyType == request.Type.Value)
                .Select(d => new PendingDiscrepancyDto
                {
                    Id = d.Id,
                    ReconciliationId = r.Id,
                    AccountId = r.AccountId,
                    ReconciliationDate = r.ReconciliationDate,
                    DiscrepancyType = d.DiscrepancyType.ToString(),
                    InternalTransactionId = d.InternalTransactionId,
                    ExternalReference = d.ExternalReference,
                    InternalAmount = d.InternalAmount,
                    ExternalAmount = d.ExternalAmount,
                    DifferenceAmount = d.DifferenceAmount,
                    InternalDate = d.InternalDate,
                    ExternalDate = d.ExternalDate,
                    Notes = d.ResolutionNotes,
                    CreatedAt = d.CreatedAt
                }))
            .OrderByDescending(d => d.CreatedAt)
            .Take(Math.Clamp(request.Limit, 1, 1000))
            .ToList();

        return Result<IReadOnlyList<PendingDiscrepancyDto>>.Success(pending);
    }
}
