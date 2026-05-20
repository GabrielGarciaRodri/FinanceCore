using MediatR;
using Microsoft.Extensions.Logging;
using FinanceCore.Application.Common.Models;
using FinanceCore.Application.Common.Behaviors;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.Application.Transactions.Queries;

#region GetTransactionById

public record GetTransactionByIdQuery(Guid Id) : IRequest<Result<TransactionDetailDto>>;

public record TransactionDetailDto
{
    public Guid Id { get; init; }
    public string ExternalId { get; init; } = null!;
    public Guid AccountId { get; init; }
    public string Type { get; init; } = null!;
    public string Status { get; init; } = null!;
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = null!;
    public DateOnly ValueDate { get; init; }
    public DateOnly BookingDate { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? CounterpartyName { get; init; }
    public string? CounterpartyAccount { get; init; }
    public string? CounterpartyBank { get; init; }
    public decimal? OriginalAmount { get; init; }
    public string? OriginalCurrency { get; init; }
    public decimal? ExchangeRateUsed { get; init; }
    public Guid? ReconciliationId { get; init; }
    public DateTimeOffset? ReconciledAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
}

public class GetTransactionByIdQueryHandler
    : IRequestHandler<GetTransactionByIdQuery, Result<TransactionDetailDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetTransactionByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TransactionDetailDto>> Handle(
        GetTransactionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var transaction = await _unitOfWork.Transactions.GetByIdAsync(request.Id, cancellationToken);

        if (transaction == null)
            return Result<TransactionDetailDto>.Failure($"Transacción no encontrada: {request.Id}");

        return Result<TransactionDetailDto>.Success(new TransactionDetailDto
        {
            Id = transaction.Id,
            ExternalId = transaction.ExternalId,
            AccountId = transaction.AccountId,
            Type = transaction.Type.ToString(),
            Status = transaction.Status.ToString(),
            Amount = transaction.Amount.Amount,
            CurrencyCode = transaction.Amount.Currency.Code,
            ValueDate = transaction.ValueDate,
            BookingDate = transaction.BookingDate,
            Description = transaction.Description,
            Category = transaction.Category,
            CounterpartyName = transaction.Counterparty?.Name,
            CounterpartyAccount = transaction.Counterparty?.AccountNumber,
            CounterpartyBank = transaction.Counterparty?.BankName,
            OriginalAmount = transaction.OriginalAmount?.Amount,
            OriginalCurrency = transaction.OriginalAmount?.Currency.Code,
            ExchangeRateUsed = transaction.ExchangeRateUsed,
            ReconciliationId = transaction.ReconciliationId,
            ReconciledAt = transaction.ReconciledAt,
            CreatedAt = transaction.CreatedAt,
            ProcessedAt = transaction.ProcessedAt
        });
    }
}

#endregion

#region SearchTransactions

public record SearchTransactionsQuery : IRequest<Result<PagedTransactionsDto>>
{
    public Guid? AccountId { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? Type { get; init; }
    public string? Status { get; init; }
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public string? Category { get; init; }
    public string? SearchText { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; } = true;
}

public record PagedTransactionsDto
{
    public IReadOnlyList<TransactionListItemDto> Items { get; init; } = Array.Empty<TransactionListItemDto>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage { get; init; }
}

public record TransactionListItemDto
{
    public Guid Id { get; init; }
    public string ExternalId { get; init; } = null!;
    public Guid AccountId { get; init; }
    public string Type { get; init; } = null!;
    public string Status { get; init; } = null!;
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = null!;
    public DateOnly ValueDate { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public bool IsReconciled { get; init; }
}

public class SearchTransactionsQueryHandler
    : IRequestHandler<SearchTransactionsQuery, Result<PagedTransactionsDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public SearchTransactionsQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PagedTransactionsDto>> Handle(
        SearchTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        var criteria = new TransactionSearchCriteria
        {
            AccountId = request.AccountId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Type = ParseEnum<TransactionType>(request.Type),
            Status = ParseEnum<TransactionStatus>(request.Status),
            MinAmount = request.MinAmount,
            MaxAmount = request.MaxAmount,
            Category = request.Category,
            SearchText = request.SearchText,
            Page = request.Page,
            PageSize = Math.Clamp(request.PageSize, 1, 200),
            SortBy = request.SortBy,
            SortDescending = request.SortDescending
        };

        var result = await _unitOfWork.Transactions.SearchAsync(criteria, cancellationToken);

        var items = result.Items.Select(t => new TransactionListItemDto
        {
            Id = t.Id,
            ExternalId = t.ExternalId,
            AccountId = t.AccountId,
            Type = t.Type.ToString(),
            Status = t.Status.ToString(),
            Amount = t.Amount.Amount,
            CurrencyCode = t.Amount.Currency.Code,
            ValueDate = t.ValueDate,
            Description = t.Description,
            Category = t.Category,
            IsReconciled = t.ReconciliationId.HasValue
        }).ToList();

        return Result<PagedTransactionsDto>.Success(new PagedTransactionsDto
        {
            Items = items,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages,
            HasNextPage = result.HasNextPage,
            HasPreviousPage = result.HasPreviousPage
        });
    }

    private static T? ParseEnum<T>(string? value) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : null;
    }
}

#endregion

#region GetAccountSummary

public record GetAccountSummaryQuery(
    Guid AccountId,
    DateOnly StartDate,
    DateOnly EndDate) : IRequest<Result<TransactionSummaryDto>>, ICacheableQuery
{
    public string CacheKey => $"acct-summary:{AccountId:N}:{StartDate:yyyyMMdd}:{EndDate:yyyyMMdd}";
    public TimeSpan CacheDuration => TimeSpan.FromSeconds(60);
}

public record TransactionSummaryDto
{
    public Guid AccountId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public int TotalCount { get; init; }
    public decimal TotalDebits { get; init; }
    public decimal TotalCredits { get; init; }
    public decimal NetChange { get; init; }
    public decimal AverageTransactionAmount { get; init; }
    public decimal LargestDebit { get; init; }
    public decimal LargestCredit { get; init; }
}

public class GetAccountSummaryQueryHandler
    : IRequestHandler<GetAccountSummaryQuery, Result<TransactionSummaryDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetAccountSummaryQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TransactionSummaryDto>> Handle(
        GetAccountSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null)
            return Result<TransactionSummaryDto>.Failure($"Cuenta no encontrada: {request.AccountId}");

        var summary = await _unitOfWork.Transactions.GetSummaryAsync(
            request.AccountId, request.StartDate, request.EndDate, cancellationToken);

        return Result<TransactionSummaryDto>.Success(new TransactionSummaryDto
        {
            AccountId = summary.AccountId,
            StartDate = summary.StartDate,
            EndDate = summary.EndDate,
            TotalCount = summary.TotalCount,
            TotalDebits = summary.TotalDebits,
            TotalCredits = summary.TotalCredits,
            NetChange = summary.NetChange,
            AverageTransactionAmount = summary.AverageTransactionAmount,
            LargestDebit = summary.LargestDebit,
            LargestCredit = summary.LargestCredit
        });
    }
}

#endregion

#region GetPendingReconciliation

public record GetPendingReconciliationQuery : IRequest<Result<IReadOnlyList<TransactionListItemDto>>>
{
    public Guid? AccountId { get; init; }
    public int Limit { get; init; } = 100;
}

public class GetPendingReconciliationQueryHandler
    : IRequestHandler<GetPendingReconciliationQuery, Result<IReadOnlyList<TransactionListItemDto>>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPendingReconciliationQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<TransactionListItemDto>>> Handle(
        GetPendingReconciliationQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Domain.Entities.Transaction> transactions;

        if (request.AccountId.HasValue)
        {
            transactions = await _unitOfWork.Transactions.GetUnreconciledByAccountAsync(
                request.AccountId.Value, cancellationToken: cancellationToken);
        }
        else
        {
            // Get all posted but unreconciled transactions
            transactions = await _unitOfWork.Transactions.GetAllAsync(
                t => t.Status == TransactionStatus.Posted && t.ReconciliationId == null,
                cancellationToken);
        }

        var items = transactions
            .Take(request.Limit)
            .Select(t => new TransactionListItemDto
            {
                Id = t.Id,
                ExternalId = t.ExternalId,
                AccountId = t.AccountId,
                Type = t.Type.ToString(),
                Status = t.Status.ToString(),
                Amount = t.Amount.Amount,
                CurrencyCode = t.Amount.Currency.Code,
                ValueDate = t.ValueDate,
                Description = t.Description,
                Category = t.Category,
                IsReconciled = false
            }).ToList();

        return Result<IReadOnlyList<TransactionListItemDto>>.Success(items);
    }
}

#endregion
