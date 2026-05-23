using MediatR;
using FinanceCore.Application.Common.Behaviors;
using FinanceCore.Application.Common.Models;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.Application.Dashboard.Queries;

/// <summary>
/// Composite query para el dashboard — todo lo que el home necesita en un solo round-trip.
/// </summary>
public record GetDashboardQuery : IRequest<Result<DashboardDto>>, ICacheableQuery
{
    /// <summary>Días hacia atrás para la time series (default 30).</summary>
    public int ActivityDays { get; init; } = 30;

    /// <summary>Cantidad de reconciliaciones recientes (default 5).</summary>
    public int RecentReconciliationsLimit { get; init; } = 5;

    public string CacheKey => $"dashboard:{ActivityDays}:{RecentReconciliationsLimit}";
    public TimeSpan CacheDuration => TimeSpan.FromSeconds(30);
}

public record DashboardDto
{
    public required IReadOnlyList<BalanceByCurrencyDto> BalancesByCurrency { get; init; }
    public required IReadOnlyList<ActivityPointDto> Activity { get; init; }
    public required IReadOnlyList<RecentReconciliationDto> RecentReconciliations { get; init; }
    public required DashboardQuickStatsDto Stats { get; init; }
}

public record BalanceByCurrencyDto
{
    public required string CurrencyCode { get; init; }
    public required decimal TotalBalance { get; init; }
    public required int AccountCount { get; init; }
}

public record ActivityPointDto
{
    public required DateOnly Date { get; init; }
    public required int Count { get; init; }
    public required decimal Debits { get; init; }      // valor absoluto (positivo)
    public required decimal Credits { get; init; }
}

public record RecentReconciliationDto
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required DateOnly Date { get; init; }
    public required string Status { get; init; }
    public required int DiscrepancyCount { get; init; }
    public required decimal DiscrepancyAmount { get; init; }
    public required bool Approved { get; init; }
}

public record DashboardQuickStatsDto
{
    public required int ActiveAccounts { get; init; }
    public required int TransactionsToday { get; init; }
    public required int PendingDiscrepancies { get; init; }
    public required int ReconciliationsLast7Days { get; init; }
}

// =============================================================================

public class GetDashboardQueryHandler : IRequestHandler<GetDashboardQuery, Result<DashboardDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDashboardQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<DashboardDto>> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var activityDays = Math.Clamp(request.ActivityDays, 1, 365);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-activityDays + 1);

        // 1. Balances por moneda
        var balances = await GetBalancesByCurrencyAsync(cancellationToken);

        // 2. Activity time series (agrupado client-side; para volumen alto migrar a Dapper)
        var activity = await GetActivitySeriesAsync(startDate, today, cancellationToken);

        // 3. Reconciliaciones recientes
        var recentRecons = await _unitOfWork.Reconciliations.SearchAsync(
            startDate: null,
            endDate: null,
            status: null,
            accountId: null,
            page: 1,
            pageSize: Math.Clamp(request.RecentReconciliationsLimit, 1, 50),
            cancellationToken: cancellationToken);

        var recentDtos = recentRecons.Select(r => new RecentReconciliationDto
        {
            Id = r.Id,
            AccountId = r.AccountId,
            Date = r.ReconciliationDate,
            Status = r.Status.ToString(),
            DiscrepancyCount = r.Discrepancies.Count,
            DiscrepancyAmount = r.DiscrepancyAmount,
            Approved = r.ApprovedAt.HasValue
        }).ToList();

        // 4. Quick stats
        var stats = await GetQuickStatsAsync(today, cancellationToken);

        return Result<DashboardDto>.Success(new DashboardDto
        {
            BalancesByCurrency = balances,
            Activity = activity,
            RecentReconciliations = recentDtos,
            Stats = stats
        });
    }

    private async Task<IReadOnlyList<BalanceByCurrencyDto>> GetBalancesByCurrencyAsync(CancellationToken ct)
    {
        var balanceMap = await _unitOfWork.Accounts.GetTotalBalancesByCurrencyAsync(ct);
        var accounts = await _unitOfWork.Accounts.GetActiveAccountsAsync(ct);

        var accountsByCurrency = accounts
            .GroupBy(a => a.Currency.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return balanceMap
            .Select(kv => new BalanceByCurrencyDto
            {
                CurrencyCode = kv.Key,
                TotalBalance = kv.Value,
                AccountCount = accountsByCurrency.TryGetValue(kv.Key, out var n) ? n : 0
            })
            .OrderByDescending(b => b.TotalBalance)
            .ToList();
    }

    private async Task<IReadOnlyList<ActivityPointDto>> GetActivitySeriesAsync(
        DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var transactions = await _unitOfWork.Transactions.SearchAsync(
            new TransactionSearchCriteria
            {
                StartDate = startDate,
                EndDate = endDate,
                Page = 1,
                PageSize = 100_000   // límite alto: el dashboard agrupa todo el rango
            },
            ct);

        return transactions.Items
            .Where(t => t.Status != TransactionStatus.Rejected && t.Status != TransactionStatus.Reversed)
            .GroupBy(t => t.ValueDate)
            .OrderBy(g => g.Key)
            .Select(g => new ActivityPointDto
            {
                Date = g.Key,
                Count = g.Count(),
                Debits = g.Where(t => t.Amount.Amount < 0).Sum(t => Math.Abs(t.Amount.Amount)),
                Credits = g.Where(t => t.Amount.Amount > 0).Sum(t => t.Amount.Amount)
            })
            .ToList();
    }

    private async Task<DashboardQuickStatsDto> GetQuickStatsAsync(DateOnly today, CancellationToken ct)
    {
        var activeAccounts = (await _unitOfWork.Accounts.GetActiveAccountsAsync(ct)).Count;

        var todayCount = await _unitOfWork.Transactions.CountAsync(
            t => t.ValueDate == today, ct);

        var withDiscrepancies = await _unitOfWork.Reconciliations.SearchAsync(
            status: ReconciliationStatus.CompletedWithDiscrepancies,
            startDate: null, endDate: null, accountId: null,
            page: 1, pageSize: 200, cancellationToken: ct);

        var pendingDisc = withDiscrepancies.Sum(r => r.Discrepancies.Count(d => !d.IsResolved));

        var sevenDaysAgo = today.AddDays(-7);
        var reconciliations7d = await _unitOfWork.Reconciliations.SearchAsync(
            startDate: sevenDaysAgo, endDate: today,
            status: null, accountId: null,
            page: 1, pageSize: 1000, cancellationToken: ct);

        return new DashboardQuickStatsDto
        {
            ActiveAccounts = activeAccounts,
            TransactionsToday = todayCount,
            PendingDiscrepancies = pendingDisc,
            ReconciliationsLast7Days = reconciliations7d.Count
        };
    }
}
