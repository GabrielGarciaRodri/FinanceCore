namespace FinanceCore.Infrastructure.Reconciliations;

/// <summary>
/// Configuración del motor de conciliación.
/// </summary>
public class ReconciliationOptions
{
    public const string SectionName = "FinanceCore:Reconciliation";

    /// <summary>
    /// Diferencia absoluta máxima permitida entre balance calculado y reportado
    /// para considerar el cierre conciliado sin discrepancia.
    /// </summary>
    public decimal BalanceTolerance { get; set; } = 0.01m;

    /// <summary>
    /// Diferencia absoluta máxima entre montos para considerar dos transacciones equivalentes.
    /// </summary>
    public decimal AmountTolerance { get; set; } = 0.01m;

    /// <summary>
    /// Ventana de tolerancia de fecha (en días) al hacer matching transacción-a-transacción.
    /// </summary>
    public int DateToleranceDays { get; set; } = 2;

    /// <summary>
    /// Si verdadero, el DailyCloseJob encolará automáticamente la conciliación
    /// para cada cuenta al cerrar el día.
    /// </summary>
    public bool AutoReconcileAfterClose { get; set; } = true;

    /// <summary>
    /// Discrepancia absoluta a partir de la cual se considera "significativa" para alertas.
    /// </summary>
    public decimal SignificantDiscrepancyThreshold { get; set; } = 1000m;

    /// <summary>
    /// Identificador que se registrará como ProcessedBy en conciliaciones automáticas.
    /// </summary>
    public string SystemProcessor { get; set; } = "SYSTEM";
}
