using Serilog.Core;
using Serilog.Events;

namespace FinanceCore.API.Logging;

/// <summary>
/// Enmascara propiedades sensibles antes de escribir logs.
/// </summary>
public sealed class SensitiveDataEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApiKey",
        "X-Api-Key",
        "Authorization",
        "Password",
        "Secret",
        "Token"
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.Count == 0)
        {
            return;
        }

        foreach (var propertyName in logEvent.Properties.Keys)
        {
            if (!SensitivePropertyNames.Contains(propertyName))
            {
                continue;
            }

            var redactedProperty = propertyFactory.CreateProperty(propertyName, "***");
            logEvent.AddOrUpdateProperty(redactedProperty);
        }
    }
}
