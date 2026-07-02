namespace FinanceCore.API.RateLimiting;

/// <summary>
/// Opciones del rate limiting por IP. La API pública corre en un free tier:
/// sin throttling, un solo cliente abusivo puede agotar la instancia, y el
/// login sin límite invita a fuerza bruta.
/// </summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Apagado en Development (dev local + suite E2E en CI hacen
    /// ráfagas legítimas); encendido por default en el resto.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Ventana fija por IP para toda la API. /health y /metrics
    /// quedan exentos (probes de Render + scraping de Prometheus).</summary>
    public int GlobalPermitLimit { get; set; } = 120;

    public int GlobalWindowSeconds { get; set; } = 60;

    /// <summary>Ventana estricta por IP para /api/auth/*.</summary>
    public int AuthPermitLimit { get; set; } = 10;

    public int AuthWindowSeconds { get; set; } = 60;
}
