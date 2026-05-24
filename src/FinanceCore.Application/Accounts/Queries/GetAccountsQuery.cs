using MediatR;
using FinanceCore.Application.Common.Behaviors;
using FinanceCore.Application.Common.Models;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.Application.Accounts.Queries;

/// <summary>
/// Query liviana para poblar selectores de cuenta en la UI (filtros de transacciones,
/// reconciliaciones, etc.). Por defecto devuelve sólo cuentas activas.
/// </summary>
public record GetAccountsQuery : IRequest<Result<IReadOnlyList<AccountListItemDto>>>, ICacheableQuery
{
    /// <summary>Si es true, incluye cuentas inactivas. Default: false.</summary>
    public bool IncludeInactive { get; init; } = false;

    public string CacheKey => $"accounts:list:active={!IncludeInactive}";
    public TimeSpan CacheDuration => TimeSpan.FromSeconds(60);
}

public record AccountListItemDto
{
    public required Guid Id { get; init; }
    public required string AccountNumber { get; init; }
    public required string AccountName { get; init; }
    public required string Type { get; init; }
    public required string CurrencyCode { get; init; }
    public required decimal CurrentBalance { get; init; }
    public required bool IsActive { get; init; }
    public required Guid InstitutionId { get; init; }
}

public class GetAccountsQueryHandler : IRequestHandler<GetAccountsQuery, Result<IReadOnlyList<AccountListItemDto>>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetAccountsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<IReadOnlyList<AccountListItemDto>>> Handle(
        GetAccountsQuery request,
        CancellationToken cancellationToken)
    {
        var accounts = request.IncludeInactive
            ? await _unitOfWork.Accounts.GetAllAsync(predicate: null, cancellationToken)
            : await _unitOfWork.Accounts.GetActiveAccountsAsync(cancellationToken);

        var items = accounts
            .Select(a => new AccountListItemDto
            {
                Id = a.Id,
                AccountNumber = a.AccountNumber,
                AccountName = a.AccountName,
                Type = a.Type.ToString(),
                CurrencyCode = a.Currency.Code,
                CurrentBalance = a.CurrentBalance.Amount,
                IsActive = a.IsActive,
                InstitutionId = a.InstitutionId
            })
            .OrderBy(a => a.AccountName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<IReadOnlyList<AccountListItemDto>>.Success(items);
    }
}
