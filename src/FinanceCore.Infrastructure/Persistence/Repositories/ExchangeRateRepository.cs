using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Persistence.Repositories;

public class ExchangeRateRepository : IExchangeRateRepository
{
    private readonly FinanceCoreDbContext _context;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(60);

    public ExchangeRateRepository(FinanceCoreDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<ExchangeRate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.ExchangeRates.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<ExchangeRate>> GetAllAsync(
        System.Linq.Expressions.Expression<Func<ExchangeRate, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.ExchangeRates.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.ToListAsync(ct);
    }

    public void Add(ExchangeRate entity) => _context.ExchangeRates.Add(entity);
    public void AddRange(IEnumerable<ExchangeRate> entities) => _context.ExchangeRates.AddRange(entities);
    public void Update(ExchangeRate entity) => _context.ExchangeRates.Update(entity);
    public void Remove(ExchangeRate entity) => _context.ExchangeRates.Remove(entity);

    public async Task<bool> ExistsAsync(
        System.Linq.Expressions.Expression<Func<ExchangeRate, bool>> predicate,
        CancellationToken ct = default)
        => await _context.ExchangeRates.AnyAsync(predicate, ct);

    public async Task<int> CountAsync(
        System.Linq.Expressions.Expression<Func<ExchangeRate, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = _context.ExchangeRates.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return await query.CountAsync(ct);
    }

    public async Task<ExchangeRate?> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default)
    {
        var key = $"fx:rate:{from}:{to}:{date:yyyy-MM-dd}";
        if (_cache.TryGetValue(key, out ExchangeRate? cached))
            return cached;

        var rate = await _context.ExchangeRates
            .Where(e => e.FromCurrency == from && e.ToCurrency == to && e.EffectiveDate <= date)
            .OrderByDescending(e => e.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        if (rate != null)
            _cache.Set(key, rate, DefaultCacheTtl);

        return rate;
    }

    public async Task<ExchangeRate?> GetLatestRateAsync(string from, string to, CancellationToken ct = default)
    {
        var key = $"fx:latest:{from}:{to}";
        if (_cache.TryGetValue(key, out ExchangeRate? cached))
            return cached;

        var rate = await _context.ExchangeRates
            .Where(e => e.FromCurrency == from && e.ToCurrency == to)
            .OrderByDescending(e => e.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        if (rate != null)
            _cache.Set(key, rate, DefaultCacheTtl);

        return rate;
    }

    public async Task<decimal> ConvertAsync(decimal amount, string from, string to, DateOnly date, CancellationToken ct = default)
    {
        if (from == to) return amount;
        var rate = await GetRateAsync(from, to, date, ct);
        if (rate == null) throw new InvalidOperationException($"Exchange rate not found: {from} to {to}");
        return Math.Round(amount * rate.Rate, 4, MidpointRounding.ToEven);
    }

    public async Task<IReadOnlyList<ExchangeRate>> GetHistoricalRatesAsync(
        string from, string to, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
        => await _context.ExchangeRates
            .Where(e => e.FromCurrency == from && e.ToCurrency == to && e.EffectiveDate >= startDate && e.EffectiveDate <= endDate)
            .OrderBy(e => e.EffectiveDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExchangeRate>> GetAllLatestRatesAsync(string baseCurrency, CancellationToken ct = default)
        => await _context.ExchangeRates
            .Where(e => e.FromCurrency == baseCurrency)
            .GroupBy(e => e.ToCurrency)
            .Select(g => g.OrderByDescending(e => e.EffectiveDate).First())
            .ToListAsync(ct);

    public async Task AddAsync(ExchangeRate rate, CancellationToken ct = default)
    {
        _cache.Remove($"fx:latest:{rate.FromCurrency}:{rate.ToCurrency}");
        _cache.Remove($"fx:rate:{rate.FromCurrency}:{rate.ToCurrency}:{rate.EffectiveDate:yyyy-MM-dd}");
        await _context.ExchangeRates.AddAsync(rate, ct);
    }

    public async Task AddRangeAsync(IEnumerable<ExchangeRate> rates, CancellationToken ct = default)
    {
        var rateList = rates as IList<ExchangeRate> ?? rates.ToList();
        foreach (var r in rateList)
        {
            _cache.Remove($"fx:latest:{r.FromCurrency}:{r.ToCurrency}");
            _cache.Remove($"fx:rate:{r.FromCurrency}:{r.ToCurrency}:{r.EffectiveDate:yyyy-MM-dd}");
        }
        await _context.ExchangeRates.AddRangeAsync(rateList, ct);
    }

    public async Task<IReadOnlyList<ExchangeRate>> GetHistoryAsync(
        string from, string to, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
        => await GetHistoricalRatesAsync(from, to, startDate, endDate, ct);
}
