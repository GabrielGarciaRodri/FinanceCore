namespace FinanceCore.Domain.ValueObjects;

/// <summary>
/// Value Object que representa una moneda según ISO 4217.
/// Incluye validaciones y datos de las monedas más comunes.
/// </summary>
public sealed class Currency : IEquatable<Currency>
{
    // Monedas predefinidas más comunes
    private static readonly Dictionary<string, CurrencyInfo> KnownCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        // América
        ["USD"] = new("USD", "US Dollar", "$", 840, 2),
        ["COP"] = new("COP", "Colombian Peso", "$", 170, 2),
        ["MXN"] = new("MXN", "Mexican Peso", "$", 484, 2),
        ["BRL"] = new("BRL", "Brazilian Real", "R$", 986, 2),
        ["ARS"] = new("ARS", "Argentine Peso", "$", 32, 2),
        ["CLP"] = new("CLP", "Chilean Peso", "$", 152, 0),
        ["PEN"] = new("PEN", "Peruvian Sol", "S/", 604, 2),
        ["CAD"] = new("CAD", "Canadian Dollar", "C$", 124, 2),
        
        // Europa
        ["EUR"] = new("EUR", "Euro", "€", 978, 2),
        ["GBP"] = new("GBP", "British Pound", "£", 826, 2),
        ["CHF"] = new("CHF", "Swiss Franc", "CHF", 756, 2),
        
        // Asia/Pacífico
        ["JPY"] = new("JPY", "Japanese Yen", "¥", 392, 0),
        ["CNY"] = new("CNY", "Chinese Yuan", "¥", 156, 2),
        ["KRW"] = new("KRW", "South Korean Won", "₩", 410, 0),
        ["INR"] = new("INR", "Indian Rupee", "₹", 356, 2),
        ["AUD"] = new("AUD", "Australian Dollar", "A$", 36, 2),
        
        // Crypto (para compatibilidad)
        ["BTC"] = new("BTC", "Bitcoin", "₿", 0, 8),
        ["ETH"] = new("ETH", "Ethereum", "Ξ", 0, 18),
    };

    /// <summary>
    /// Código de moneda ISO 4217 (3 caracteres).
    /// </summary>
    public string Code { get; }
    
    /// <summary>
    /// Nombre descriptivo de la moneda.
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// Símbolo de la moneda para mostrar.
    /// </summary>
    public string Symbol { get; }
    
    /// <summary>
    /// Código numérico ISO 4217.
    /// </summary>
    public int NumericCode { get; }
    
    /// <summary>
    /// Número de decimales estándar para la moneda.
    /// </summary>
    public int DecimalPlaces { get; }

    private Currency(string code, string name, string symbol, int numericCode, int decimalPlaces)
    {
        Code = code.ToUpperInvariant();
        Name = name;
        Symbol = symbol;
        NumericCode = numericCode;
        DecimalPlaces = decimalPlaces;
    }

    #region Factory Methods

    /// <summary>
    /// Crea una instancia de Currency desde un código ISO 4217.
    /// </summary>
    public static Currency FromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("El código de moneda no puede estar vacío.", nameof(code));

        code = code.Trim().ToUpperInvariant();
        
        if (code.Length != 3)
            throw new ArgumentException($"El código de moneda debe tener 3 caracteres: {code}", nameof(code));

        if (KnownCurrencies.TryGetValue(code, out var info))
        {
            return new Currency(info.Code, info.Name, info.Symbol, info.NumericCode, info.DecimalPlaces);
        }

        // Moneda desconocida - crear con valores por defecto
        return new Currency(code, code, code, 0, 2);
    }

    /// <summary>
    /// Intenta crear una Currency desde un código.
    /// </summary>
    public static bool TryFromCode(string code, out Currency? currency)
    {
        currency = null;
        
        try
        {
            currency = FromCode(code);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Crea una moneda personalizada (útil para testing o monedas no estándar).
    /// </summary>
    public static Currency Custom(string code, string name, string symbol, int decimalPlaces = 2)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 3)
            throw new ArgumentException("El código debe tener exactamente 3 caracteres.", nameof(code));

        return new Currency(code.ToUpperInvariant(), name, symbol, 0, decimalPlaces);
    }

    #endregion

    #region Predefined Currencies

    public static Currency USD => FromCode("USD");
    public static Currency EUR => FromCode("EUR");
    public static Currency COP => FromCode("COP");
    public static Currency GBP => FromCode("GBP");
    public static Currency JPY => FromCode("JPY");
    public static Currency MXN => FromCode("MXN");
    public static Currency BRL => FromCode("BRL");

    #endregion

    #region Utility Methods

    /// <summary>
    /// Verifica si el código de moneda es válido (conocido por el sistema).
    /// </summary>
    public static bool IsKnownCurrency(string code)
    {
        return !string.IsNullOrWhiteSpace(code) && 
               KnownCurrencies.ContainsKey(code.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Obtiene todas las monedas conocidas.
    /// </summary>
    public static IEnumerable<Currency> GetAllKnownCurrencies()
    {
        return KnownCurrencies.Values.Select(info => 
            new Currency(info.Code, info.Name, info.Symbol, info.NumericCode, info.DecimalPlaces));
    }

    /// <summary>
    /// Verifica si esta moneda tiene subunidades (centavos, etc.).
    /// </summary>
    public bool HasDecimalPlaces => DecimalPlaces > 0;

    /// <summary>
    /// Retorna el factor de conversión a la unidad menor.
    /// Por ejemplo, para USD retorna 100 (100 centavos = 1 dólar).
    /// </summary>
    public decimal MinorUnitFactor => (decimal)Math.Pow(10, DecimalPlaces);

    #endregion

    #region Equality

    public bool Equals(Currency? other)
    {
        if (other is null) return false;
        return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is Currency currency && Equals(currency);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Code);

    public static bool operator ==(Currency? left, Currency? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(Currency? left, Currency? right) => !(left == right);

    #endregion

    public override string ToString() => Code;

    /// <summary>
    /// Información interna de moneda.
    /// </summary>
    private record CurrencyInfo(string Code, string Name, string Symbol, int NumericCode, int DecimalPlaces);
}
