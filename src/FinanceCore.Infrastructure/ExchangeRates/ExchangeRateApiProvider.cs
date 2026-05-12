using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FinanceCore.Infrastructure.BackgroundJobs.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceCore.Infrastructure.ExchangeRates;

public class ExchangeRateApiProvider : IExchangeRateProvider
{
    private readonly HttpClient _httpClient;
    private readonly ExchangeRateOptions _options;
    private readonly ILogger<ExchangeRateApiProvider> _logger;

    public ExchangeRateApiProvider(
        HttpClient httpClient,
        IOptions<ExchangeRateOptions> options,
        ILogger<ExchangeRateApiProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "ExchangeRateApi";

    public async Task<IEnumerable<ExchangeRateData>> GetLatestRatesAsync(
        string baseCurrency,
        string[] targetCurrencies,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new ExchangeRateProviderException(
                "ExchangeRate API key no configurada. Configurar FinanceCore:ExchangeRates:ApiKey.");

        var url = $"{_options.BaseUrl.TrimEnd('/')}/{_options.ApiKey}/latest/{baseCurrency}";
        _logger.LogInformation("Consultando tipos de cambio para {Base} desde {Provider}", baseCurrency, ProviderName);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ExchangeRateProviderException(
                $"Error de red al consultar {ProviderName}: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ExchangeRateProviderException(
                $"{ProviderName} respondió {(int)response.StatusCode} para base {baseCurrency}.");
        }

        ExchangeRateApiResponse? apiResponse;
        try
        {
            apiResponse = await response.Content.ReadFromJsonAsync<ExchangeRateApiResponse>(
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw new ExchangeRateProviderException(
                $"Respuesta inválida de {ProviderName}: {ex.Message}", ex);
        }

        if (apiResponse is null || apiResponse.Result != "success" || apiResponse.ConversionRates is null)
        {
            throw new ExchangeRateProviderException(
                $"{ProviderName} devolvió resultado inválido para base {baseCurrency}.");
        }

        var targetSet = new HashSet<string>(targetCurrencies, StringComparer.OrdinalIgnoreCase);

        var rates = apiResponse.ConversionRates
            .Where(kvp => targetSet.Contains(kvp.Key) && kvp.Key != baseCurrency && kvp.Value > 0)
            .Select(kvp => new ExchangeRateData(baseCurrency, kvp.Key.ToUpperInvariant(), kvp.Value))
            .ToList();

        _logger.LogInformation(
            "Obtenidos {Count} tipos de cambio desde {Provider} para base {Base}",
            rates.Count, ProviderName, baseCurrency);

        return rates;
    }
}

internal sealed class ExchangeRateApiResponse
{
    [JsonPropertyName("result")]
    public string Result { get; set; } = "";

    [JsonPropertyName("base_code")]
    public string BaseCode { get; set; } = "";

    [JsonPropertyName("conversion_rates")]
    public Dictionary<string, decimal>? ConversionRates { get; set; }
}
