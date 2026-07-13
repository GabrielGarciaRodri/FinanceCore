using MediatR;
using FinanceCore.Application.Common.Models;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.Application.Reconciliations.Queries;

#region DTOs

public record ReconciliationDto
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public DateOnly ReconciliationDate { get; init; }
    public string Status { get; init; } = null!;
    public int TotalInternalRecords { get; init; }
    public int TotalExternalRecords { get; init; }
    public int MatchedCount { get; init; }
    public int UnmatchedInternal { get; init; }
    public int UnmatchedExternal { get; init; }
    public decimal TotalInternalAmount { get; init; }
    public decimal TotalExternalAmount { get; init; }
    public decimal DiscrepancyAmount { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long? DurationMs { get; init; }
    public string ProcessedBy { get; init; } = null!;
    public string? ApprovedBy { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<ReconciliationDiscrepancyDto> Discrepancies { get; init; } = Array.Empty<ReconciliationDiscrepancyDto>();
    public IReadOnlyList<ReconciliationMatchGroupDto> MatchGroups { get; init; } = Array.Empty<ReconciliationMatchGroupDto>();
}

/// <summary>
/// Payout de pasarela conciliado por matching N:1: la línea de extracto,
/// las ventas agrupadas y la comisión explicada.
/// </summary>
public record ReconciliationMatchGroupDto
{
    public Guid Id { get; init; }
    public Guid SourceProfileId { get; init; }
    public string ExternalReference { get; init; } = null!;
    public decimal PayoutAmount { get; init; }
    public DateOnly PayoutDate { get; init; }
    public int GroupedCount { get; init; }
    public decimal GroupedAmount { get; init; }
    public decimal FeeAmount { get; init; }
    public decimal FeePercent { get; init; }
    public Guid? FeeTransactionId { get; init; }
    public DateOnly WindowStart { get; init; }
    public DateOnly WindowEnd { get; init; }
    public IReadOnlyList<ReconciliationMatchGroupItemDto> Items { get; init; } = Array.Empty<ReconciliationMatchGroupItemDto>();
}

public record ReconciliationMatchGroupItemDto
{
    public Guid TransactionId { get; init; }
    public decimal Amount { get; init; }
}

public record ReconciliationDiscrepancyDto
{
    public Guid Id { get; init; }
    public string DiscrepancyType { get; init; } = null!;
    public Guid? InternalTransactionId { get; init; }
    public string? ExternalReference { get; init; }
    public decimal? InternalAmount { get; init; }
    public decimal? ExternalAmount { get; init; }
    public decimal? DifferenceAmount { get; init; }
    public DateOnly? InternalDate { get; init; }
    public DateOnly? ExternalDate { get; init; }
    public bool IsResolved { get; init; }
    public string? ResolutionType { get; init; }
    public string? ResolutionNotes { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}

#endregion

#region GetReconciliationByAccountAndDate

public record GetReconciliationByAccountAndDateQuery(Guid AccountId, DateOnly Date)
    : IRequest<Result<ReconciliationDto>>;

public class GetReconciliationByAccountAndDateQueryHandler
    : IRequestHandler<GetReconciliationByAccountAndDateQuery, Result<ReconciliationDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetReconciliationByAccountAndDateQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<ReconciliationDto>> Handle(
        GetReconciliationByAccountAndDateQuery request,
        CancellationToken cancellationToken)
    {
        var reconciliation = await _unitOfWork.Reconciliations
            .GetByAccountAndDateAsync(request.AccountId, request.Date, cancellationToken);

        if (reconciliation == null)
            return Result<ReconciliationDto>.Failure(
                $"No existe conciliación para cuenta {request.AccountId} en {request.Date:yyyy-MM-dd}");

        return Result<ReconciliationDto>.Success(ReconciliationMapper.ToDto(reconciliation));
    }
}

#endregion

#region SearchReconciliations

public record SearchReconciliationsQuery : IRequest<Result<PagedResult<ReconciliationDto>>>
{
    public Guid? AccountId { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public ReconciliationStatus? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public class SearchReconciliationsQueryHandler
    : IRequestHandler<SearchReconciliationsQuery, Result<PagedResult<ReconciliationDto>>>
{
    private readonly IUnitOfWork _unitOfWork;

    public SearchReconciliationsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<PagedResult<ReconciliationDto>>> Handle(
        SearchReconciliationsQuery request,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var page = Math.Max(1, request.Page);

        // Total y página se resuelven con dos queries — el repo evita doble round-trip
        // a la base sólo si fuese el caso de proyecciones complejas; acá es trivial.
        var totalCount = await _unitOfWork.Reconciliations.CountAsync(
            request.StartDate,
            request.EndDate,
            request.Status,
            request.AccountId,
            cancellationToken);

        var results = await _unitOfWork.Reconciliations.SearchAsync(
            request.StartDate,
            request.EndDate,
            request.Status,
            request.AccountId,
            page,
            pageSize,
            cancellationToken);

        return Result<PagedResult<ReconciliationDto>>.Success(new PagedResult<ReconciliationDto>
        {
            Items = results.Select(ReconciliationMapper.ToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }
}

#endregion

#region GetReconciliationById

public record GetReconciliationByIdQuery(Guid Id)
    : IRequest<Result<ReconciliationDto>>;

public class GetReconciliationByIdQueryHandler
    : IRequestHandler<GetReconciliationByIdQuery, Result<ReconciliationDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetReconciliationByIdQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<ReconciliationDto>> Handle(
        GetReconciliationByIdQuery request,
        CancellationToken cancellationToken)
    {
        var reconciliation = await _unitOfWork.Reconciliations
            .GetByIdAsync(request.Id, cancellationToken);

        if (reconciliation == null)
            return Result<ReconciliationDto>.Failure(
                $"No existe conciliación con id {request.Id}");

        return Result<ReconciliationDto>.Success(ReconciliationMapper.ToDto(reconciliation));
    }
}

#endregion

internal static class ReconciliationMapper
{
    public static ReconciliationDto ToDto(Domain.Entities.Reconciliation r) => new()
    {
        Id = r.Id,
        AccountId = r.AccountId,
        ReconciliationDate = r.ReconciliationDate,
        Status = r.Status.ToString(),
        TotalInternalRecords = r.TotalInternalRecords,
        TotalExternalRecords = r.TotalExternalRecords,
        MatchedCount = r.MatchedCount,
        UnmatchedInternal = r.UnmatchedInternal,
        UnmatchedExternal = r.UnmatchedExternal,
        TotalInternalAmount = r.TotalInternalAmount,
        TotalExternalAmount = r.TotalExternalAmount,
        DiscrepancyAmount = r.DiscrepancyAmount,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        DurationMs = r.DurationMs,
        ProcessedBy = r.ProcessedBy,
        ApprovedBy = r.ApprovedBy,
        Notes = r.Notes,
        Discrepancies = r.Discrepancies.Select(d => new ReconciliationDiscrepancyDto
        {
            Id = d.Id,
            DiscrepancyType = d.DiscrepancyType.ToString(),
            InternalTransactionId = d.InternalTransactionId,
            ExternalReference = d.ExternalReference,
            InternalAmount = d.InternalAmount,
            ExternalAmount = d.ExternalAmount,
            DifferenceAmount = d.DifferenceAmount,
            InternalDate = d.InternalDate,
            ExternalDate = d.ExternalDate,
            IsResolved = d.IsResolved,
            ResolutionType = d.ResolutionType?.ToString(),
            ResolutionNotes = d.ResolutionNotes,
            ResolvedAt = d.ResolvedAt
        }).ToList(),
        MatchGroups = r.MatchGroups.Select(g => new ReconciliationMatchGroupDto
        {
            Id = g.Id,
            SourceProfileId = g.SourceProfileId,
            ExternalReference = g.ExternalReference,
            PayoutAmount = g.PayoutAmount,
            PayoutDate = g.PayoutDate,
            GroupedCount = g.GroupedCount,
            GroupedAmount = g.GroupedAmount,
            FeeAmount = g.FeeAmount,
            FeePercent = g.FeePercent,
            FeeTransactionId = g.FeeTransactionId,
            WindowStart = g.WindowStart,
            WindowEnd = g.WindowEnd,
            Items = g.Items.Select(i => new ReconciliationMatchGroupItemDto
            {
                TransactionId = i.TransactionId,
                Amount = i.Amount
            }).ToList()
        }).ToList()
    };
}
