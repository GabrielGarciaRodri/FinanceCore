namespace FinanceCore.Infrastructure.ExchangeRates;

public class ExchangeRateOptions
{
    public const string SectionName = "FinanceCore:ExchangeRates";

    public string Provider { get; set; } = "ExchangeRateApi";
    public string BaseUrl { get; set; } = "https://v6.exchangerate-api.com/v6";
    public string ApiKey { get; set; } = "";
    public string BaseCurrency { get; set; } = "USD";
    public int UpdateIntervalHours { get; set; } = 1;
    public int CacheDurationMinutes { get; set; } = 60;
    public string[] SupportedCurrencies { get; set; } = ["USD", "EUR", "COP", "MXN", "BRL"];
    public int RetryAttempts { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 10;
}

public class ExchangeRateProviderException : Exception
{
    public ExchangeRateProviderException(string message) : base(message) { }
    public ExchangeRateProviderException(string message, Exception inner) : base(message, inner) { }
}
