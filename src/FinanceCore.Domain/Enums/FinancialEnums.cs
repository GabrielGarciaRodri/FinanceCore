namespace FinanceCore.Domain.Enums;

/// <summary>
/// Tipo de cuenta financiera.
/// </summary>
public enum AccountType
{
    /// <summary>Cuenta corriente</summary>
    Checking = 1,
    
    /// <summary>Cuenta de ahorro</summary>
    Savings = 2,
    
    /// <summary>Cuenta de inversión</summary>
    Investment = 3,
    
    /// <summary>Línea de crédito</summary>
    Credit = 4,
    
    /// <summary>Préstamo</summary>
    Loan = 5,
    
    /// <summary>Cuenta de tesorería</summary>
    Treasury = 6
}

/// <summary>
/// Tipo de transacción financiera.
/// </summary>
public enum TransactionType
{
    /// <summary>Débito - salida de fondos</summary>
    Debit = 1,
    
    /// <summary>Crédito - entrada de fondos</summary>
    Credit = 2,
    
    /// <summary>Transferencia saliente</summary>
    TransferOut = 3,
    
    /// <summary>Transferencia entrante</summary>
    TransferIn = 4,
    
    /// <summary>Comisión bancaria</summary>
    Fee = 5,
    
    /// <summary>Intereses (ganados o pagados)</summary>
    Interest = 6,
    
    /// <summary>Ajuste contable</summary>
    Adjustment = 7
}

/// <summary>
/// Estado de una transacción en su ciclo de vida.
/// </summary>
public enum TransactionStatus
{
    /// <summary>Recién ingresada, pendiente de validación</summary>
    Pending = 1,
    
    /// <summary>En proceso de validación/transformación</summary>
    Processing = 2,
    
    /// <summary>Validada y lista para contabilizar</summary>
    Validated = 3,
    
    /// <summary>Contabilizada (afectó saldos)</summary>
    Posted = 4,
    
    /// <summary>Conciliada con fuente externa</summary>
    Reconciled = 5,
    
    /// <summary>Rechazada por validación</summary>
    Rejected = 6,
    
    /// <summary>Reversada (se aplicó reverso)</summary>
    Reversed = 7
}

/// <summary>
/// Estado del proceso de conciliación.
/// </summary>
public enum ReconciliationStatus
{
    /// <summary>Pendiente de iniciar</summary>
    Pending = 1,
    
    /// <summary>Proceso en ejecución</summary>
    InProgress = 2,
    
    /// <summary>Completado sin descuadres</summary>
    Completed = 3,
    
    /// <summary>Completado con descuadres identificados</summary>
    CompletedWithDiscrepancies = 4,
    
    /// <summary>Fallido por error</summary>
    Failed = 5
}

/// <summary>
/// Tipo de fuente de datos para ingesta.
/// </summary>
public enum SourceType
{
    /// <summary>API externa (REST, SOAP)</summary>
    Api = 1,
    
    /// <summary>Archivo CSV</summary>
    CsvFile = 2,
    
    /// <summary>Archivo Excel</summary>
    ExcelFile = 3,
    
    /// <summary>Transferencia SFTP</summary>
    Sftp = 4,
    
    /// <summary>Entrada manual por usuario</summary>
    Manual = 5,
    
    /// <summary>Generado por el sistema</summary>
    System = 6
}

/// <summary>
/// Tipo de discrepancia en conciliación.
/// </summary>
public enum DiscrepancyType
{
    /// <summary>Existe en sistema pero no en fuente externa</summary>
    MissingExternal = 1,
    
    /// <summary>Existe en fuente externa pero no en sistema</summary>
    MissingInternal = 2,
    
    /// <summary>El monto no coincide</summary>
    AmountMismatch = 3,
    
    /// <summary>La fecha no coincide</summary>
    DateMismatch = 4,
    
    /// <summary>Posible duplicado</summary>
    PossibleDuplicate = 5,
    
    /// <summary>Datos de referencia no coinciden</summary>
    ReferenceMismatch = 6
}

/// <summary>
/// Estado de resolución de una discrepancia.
/// </summary>
public enum ResolutionType
{
    /// <summary>Pendiente de resolución</summary>
    Pending = 1,
    
    /// <summary>Coincidencia manual encontrada</summary>
    MatchedManually = 2,
    
    /// <summary>Se creó ajuste contable</summary>
    AdjustmentCreated = 3,
    
    /// <summary>Ignorada (con justificación)</summary>
    Ignored = 4,
    
    /// <summary>En investigación</summary>
    UnderInvestigation = 5,
    
    /// <summary>Escalada a supervisor</summary>
    Escalated = 6
}

/// <summary>
/// Extensiones útiles para los enums.
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// Determina si el tipo de transacción representa salida de fondos.
    /// </summary>
    public static bool IsDebitType(this TransactionType type)
    {
        return type is TransactionType.Debit 
            or TransactionType.TransferOut 
            or TransactionType.Fee;
    }

    /// <summary>
    /// Determina si el tipo de transacción representa entrada de fondos.
    /// </summary>
    public static bool IsCreditType(this TransactionType type)
    {
        return type is TransactionType.Credit 
            or TransactionType.TransferIn 
            or TransactionType.Interest;
    }

    /// <summary>
    /// Determina si la transacción está en un estado final.
    /// </summary>
    public static bool IsFinalState(this TransactionStatus status)
    {
        return status is TransactionStatus.Reconciled 
            or TransactionStatus.Rejected 
            or TransactionStatus.Reversed;
    }

    /// <summary>
    /// Determina si la transacción puede ser modificada.
    /// </summary>
    public static bool IsModifiable(this TransactionStatus status)
    {
        return status is TransactionStatus.Pending 
            or TransactionStatus.Processing;
    }

    /// <summary>
    /// Determina si la conciliación terminó (exitosamente o no).
    /// </summary>
    public static bool IsTerminal(this ReconciliationStatus status)
    {
        return status is ReconciliationStatus.Completed 
            or ReconciliationStatus.CompletedWithDiscrepancies 
            or ReconciliationStatus.Failed;
    }
}
