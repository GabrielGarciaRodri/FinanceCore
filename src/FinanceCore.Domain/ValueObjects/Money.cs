using System.Globalization;

namespace FinanceCore.Domain.ValueObjects;

/// <summary>
/// Value Object que representa un valor monetario con su moneda.
/// INMUTABLE - Todas las operaciones retornan nuevas instancias.
/// Implementa redondeo bancario y validaciones de precisión.
/// </summary>
public sealed class Money : IEquatable<Money>, IComparable<Money>
{
    // Constantes de precisión financiera
    private const int DecimalPlaces = 4;
    private const int DisplayDecimalPlaces = 2;
    
    /// <summary>
    /// Valor monetario. NUNCA usar float/double para dinero.
    /// </summary>
    public decimal Amount { get; }
    
    /// <summary>
    /// Código de moneda ISO 4217 (USD, EUR, COP, etc.)
    /// </summary>
    public Currency Currency { get; }

    private Money(decimal amount, Currency currency)
    {
        // Redondeo bancario (MidpointRounding.ToEven) - estándar financiero
        Amount = Math.Round(amount, DecimalPlaces, MidpointRounding.ToEven);
        Currency = currency;
    }

    #region Factory Methods

    /// <summary>
    /// Crea una instancia de Money con validaciones.
    /// </summary>
    public static Money Create(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(amount, currency);
    }

    /// <summary>
    /// Crea una instancia de Money desde string de moneda.
    /// </summary>
    public static Money Create(decimal amount, string currencyCode)
    {
        var currency = Currency.FromCode(currencyCode);
        return new Money(amount, currency);
    }

    /// <summary>
    /// Crea Money con valor cero.
    /// </summary>
    public static Money Zero(Currency currency) => new(0, currency);

    /// <summary>
    /// Crea Money con valor cero desde código de moneda.
    /// </summary>
    public static Money Zero(string currencyCode) => 
        new(0, Currency.FromCode(currencyCode));

    /// <summary>
    /// Intenta parsear un string a Money.
    /// </summary>
    public static bool TryParse(string value, string currencyCode, out Money? result)
    {
        result = null;
        
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Intentar parsear con cultura invariante
        if (!decimal.TryParse(
            value.Replace(",", ""), 
            NumberStyles.Currency, 
            CultureInfo.InvariantCulture, 
            out var amount))
        {
            return false;
        }

        try
        {
            result = Create(amount, currencyCode);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Arithmetic Operations

    /// <summary>
    /// Suma dos valores monetarios. Deben ser la misma moneda.
    /// </summary>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>
    /// Resta dos valores monetarios. Deben ser la misma moneda.
    /// </summary>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>
    /// Multiplica por un factor (para cálculos de porcentajes, tasas, etc.)
    /// </summary>
    public Money Multiply(decimal factor)
    {
        return new Money(Amount * factor, Currency);
    }

    /// <summary>
    /// Divide por un factor.
    /// </summary>
    public Money Divide(decimal divisor)
    {
        if (divisor == 0)
            throw new DivideByZeroException("No se puede dividir por cero.");
            
        return new Money(Amount / divisor, Currency);
    }

    /// <summary>
    /// Calcula el porcentaje de este valor.
    /// </summary>
    public Money Percentage(decimal percent)
    {
        return Multiply(percent / 100m);
    }

    /// <summary>
    /// Valor absoluto.
    /// </summary>
    public Money Abs() => new(Math.Abs(Amount), Currency);

    /// <summary>
    /// Negación del valor.
    /// </summary>
    public Money Negate() => new(-Amount, Currency);

    #endregion

    #region Currency Conversion

    /// <summary>
    /// Convierte a otra moneda usando la tasa proporcionada.
    /// </summary>
    /// <param name="targetCurrency">Moneda destino</param>
    /// <param name="exchangeRate">Tasa de cambio (cuántas unidades destino por 1 origen)</param>
    /// <returns>Nuevo Money en la moneda destino</returns>
    public Money ConvertTo(Currency targetCurrency, decimal exchangeRate)
    {
        if (exchangeRate <= 0)
            throw new ArgumentException("La tasa de cambio debe ser positiva.", nameof(exchangeRate));

        if (Currency.Equals(targetCurrency))
            return this; // No conversion needed

        var convertedAmount = Amount * exchangeRate;
        return new Money(convertedAmount, targetCurrency);
    }

    /// <summary>
    /// Verifica si se puede operar con otro Money (misma moneda).
    /// </summary>
    public bool CanOperateWith(Money other) => Currency.Equals(other.Currency);

    #endregion

    #region Comparison Operations

    public bool IsZero => Amount == 0;
    public bool IsPositive => Amount > 0;
    public bool IsNegative => Amount < 0;

    public bool IsGreaterThan(Money other)
    {
        EnsureSameCurrency(other);
        return Amount > other.Amount;
    }

    public bool IsGreaterThanOrEqual(Money other)
    {
        EnsureSameCurrency(other);
        return Amount >= other.Amount;
    }

    public bool IsLessThan(Money other)
    {
        EnsureSameCurrency(other);
        return Amount < other.Amount;
    }

    public bool IsLessThanOrEqual(Money other)
    {
        EnsureSameCurrency(other);
        return Amount <= other.Amount;
    }

    #endregion

    #region Operators

    public static Money operator +(Money left, Money right) => left.Add(right);
    public static Money operator -(Money left, Money right) => left.Subtract(right);
    public static Money operator *(Money money, decimal factor) => money.Multiply(factor);
    public static Money operator *(decimal factor, Money money) => money.Multiply(factor);
    public static Money operator /(Money money, decimal divisor) => money.Divide(divisor);
    public static Money operator -(Money money) => money.Negate();

    public static bool operator ==(Money? left, Money? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(Money? left, Money? right) => !(left == right);

    public static bool operator >(Money left, Money right) => left.IsGreaterThan(right);
    public static bool operator <(Money left, Money right) => left.IsLessThan(right);
    public static bool operator >=(Money left, Money right) => left.IsGreaterThanOrEqual(right);
    public static bool operator <=(Money left, Money right) => left.IsLessThanOrEqual(right);

    #endregion

    #region Equality and Comparison

    public bool Equals(Money? other)
    {
        if (other is null) return false;
        return Amount == other.Amount && Currency.Equals(other.Currency);
    }

    public override bool Equals(object? obj) => obj is Money money && Equals(money);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    public int CompareTo(Money? other)
    {
        if (other is null) return 1;
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    #endregion

    #region Formatting

    /// <summary>
    /// Formatea para mostrar al usuario (2 decimales).
    /// </summary>
    public string ToDisplayString()
    {
        return $"{Currency.Symbol}{Amount.ToString($"N{DisplayDecimalPlaces}", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Formatea con código de moneda.
    /// </summary>
    public string ToStringWithCode()
    {
        return $"{Amount.ToString($"N{DisplayDecimalPlaces}", CultureInfo.InvariantCulture)} {Currency.Code}";
    }

    /// <summary>
    /// Formatea para almacenamiento/transmisión (4 decimales, sin formato).
    /// </summary>
    public string ToStorageString()
    {
        return $"{Amount.ToString($"F{DecimalPlaces}", CultureInfo.InvariantCulture)}|{Currency.Code}";
    }

    public override string ToString() => ToDisplayString();

    #endregion

    #region Helpers

    private void EnsureSameCurrency(Money other)
    {
        if (!Currency.Equals(other.Currency))
        {
            throw new InvalidOperationException(
                $"No se pueden operar valores de diferentes monedas: {Currency.Code} vs {other.Currency.Code}. " +
                "Realice una conversión de moneda primero.");
        }
    }

    /// <summary>
    /// Distribuye un monto entre N partes, manejando el residuo correctamente.
    /// Útil para distribución de comisiones, splits, etc.
    /// </summary>
    /// <param name="parts">Número de partes</param>
    /// <returns>Array con la distribución</returns>
    public Money[] Allocate(int parts)
    {
        if (parts <= 0)
            throw new ArgumentException("El número de partes debe ser mayor a cero.", nameof(parts));

        var result = new Money[parts];
        var lowResult = new Money(Math.Floor(Amount * 10000 / parts) / 10000, Currency);
        var remainder = Amount - (lowResult.Amount * parts);
        
        // Distribuir el residuo centavo a centavo
        var remainderCents = (int)(remainder * 10000);
        
        for (var i = 0; i < parts; i++)
        {
            result[i] = lowResult;
            if (i < remainderCents)
            {
                result[i] = result[i].Add(new Money(0.0001m, Currency));
            }
        }

        return result;
    }

    #endregion
}

/// <summary>
/// Extension methods para colecciones de Money.
/// </summary>
public static class MoneyExtensions
{
    /// <summary>
    /// Suma una colección de Money. Todos deben ser la misma moneda.
    /// </summary>
    public static Money Sum(this IEnumerable<Money> source)
    {
        var list = source.ToList();
        
        if (list.Count == 0)
            throw new InvalidOperationException("No se puede sumar una colección vacía de Money.");

        return list.Skip(1).Aggregate(list.First(), (acc, m) => acc.Add(m));
    }

    /// <summary>
    /// Suma una colección de Money o retorna Zero si está vacía.
    /// </summary>
    public static Money SumOrZero(this IEnumerable<Money> source, Currency defaultCurrency)
    {
        var list = source.ToList();
        return list.Count == 0 
            ? Money.Zero(defaultCurrency) 
            : list.Sum();
    }
}
