using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Exceptions;
using FinanceCore.Domain.ValueObjects;

namespace FinanceCore.Domain.Entities;

/// <summary>
/// Representa una cuenta financiera en el sistema.
/// Aggregate Root - todas las operaciones de saldo pasan por aquí.
/// </summary>
public class Account : BaseEntity, IAggregateRoot
{
    #region Properties

    /// <summary>
    /// Número de cuenta (formato IBAN compatible, hasta 34 caracteres).
    /// </summary>
    public string AccountNumber { get; private set; } = null!;
    
    /// <summary>
    /// Tipo de cuenta.
    /// </summary>
    public AccountType Type { get; private set; }
    
    /// <summary>
    /// Nombre descriptivo de la cuenta.
    /// </summary>
    public string AccountName { get; private set; } = null!;
    
    /// <summary>
    /// Moneda de la cuenta.
    /// </summary>
    public Currency Currency { get; private set; } = null!;
    
    /// <summary>
    /// Institución financiera.
    /// </summary>
    public Guid InstitutionId { get; private set; }

    /// <summary>
    /// Saldo actual (contable).
    /// </summary>
    public Money CurrentBalance { get; private set; } = null!;
    
    /// <summary>
    /// Saldo disponible (puede diferir del actual por retenciones, etc.).
    /// </summary>
    public Money AvailableBalance { get; private set; } = null!;

    /// <summary>
    /// Indica si la cuenta está activa.
    /// </summary>
    public bool IsActive { get; private set; }
    
    /// <summary>
    /// Fecha de apertura de la cuenta.
    /// </summary>
    public DateOnly? OpenedAt { get; private set; }
    
    /// <summary>
    /// Fecha de cierre de la cuenta (si aplica).
    /// </summary>
    public DateOnly? ClosedAt { get; private set; }

    /// <summary>
    /// Versión para control de concurrencia optimista.
    /// </summary>
    public int Version { get; private set; }

    // Navigation properties
    public virtual Institution? Institution { get; private set; }
    public virtual ICollection<Transaction> Transactions { get; private set; } = new List<Transaction>();
    public virtual ICollection<DailyBalance> DailyBalances { get; private set; } = new List<DailyBalance>();

    #endregion

    #region Constructors

    /// <summary>
    /// Constructor privado para EF Core.
    /// </summary>
    private Account() { }

    /// <summary>
    /// Crea una nueva cuenta financiera.
    /// </summary>
    private Account(
        string accountNumber,
        AccountType type,
        string accountName,
        Currency currency,
        Guid institutionId,
        decimal initialBalance = 0)
    {
        ValidateAccountNumber(accountNumber);
        
        if (string.IsNullOrWhiteSpace(accountName))
            throw new DomainException("El nombre de la cuenta es requerido.");
            
        if (institutionId == Guid.Empty)
            throw new DomainException("La institución financiera es requerida.");

        Id = Guid.NewGuid();
        AccountNumber = accountNumber.Trim().ToUpperInvariant();
        Type = type;
        AccountName = accountName.Trim();
        Currency = currency;
        InstitutionId = institutionId;
        CurrentBalance = Money.Create(initialBalance, currency);
        AvailableBalance = Money.Create(initialBalance, currency);
        IsActive = true;
        OpenedAt = DateOnly.FromDateTime(DateTime.Today);
        Version = 1;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Crea una cuenta corriente.
    /// </summary>
    public static Account CreateCheckingAccount(
        string accountNumber,
        string accountName,
        string currencyCode,
        Guid institutionId,
        decimal initialBalance = 0)
    {
        return new Account(
            accountNumber,
            AccountType.Checking,
            accountName,
            Currency.FromCode(currencyCode),
            institutionId,
            initialBalance);
    }

    /// <summary>
    /// Crea una cuenta de ahorro.
    /// </summary>
    public static Account CreateSavingsAccount(
        string accountNumber,
        string accountName,
        string currencyCode,
        Guid institutionId,
        decimal initialBalance = 0)
    {
        return new Account(
            accountNumber,
            AccountType.Savings,
            accountName,
            Currency.FromCode(currencyCode),
            institutionId,
            initialBalance);
    }

    /// <summary>
    /// Crea una cuenta de tesorería.
    /// </summary>
    public static Account CreateTreasuryAccount(
        string accountNumber,
        string accountName,
        string currencyCode,
        Guid institutionId)
    {
        return new Account(
            accountNumber,
            AccountType.Treasury,
            accountName,
            Currency.FromCode(currencyCode),
            institutionId,
            0);
    }

    #endregion

    #region Balance Operations

    /// <summary>
    /// Aplica una transacción al saldo de la cuenta.
    /// Esta operación es idempotente si se usa el mismo ID de transacción.
    /// </summary>
    public void ApplyTransaction(Transaction transaction)
    {
        if (transaction.AccountId != Id)
            throw new DomainException("La transacción no pertenece a esta cuenta.");

        if (!IsActive)
            throw new DomainException("No se pueden aplicar transacciones a una cuenta inactiva.");

        if (!transaction.Amount.Currency.Equals(Currency))
            throw new DomainException(
                $"La moneda de la transacción ({transaction.Amount.Currency.Code}) " +
                $"no coincide con la moneda de la cuenta ({Currency.Code}).");

        // Aplicar al saldo
        var newBalance = CurrentBalance.Add(transaction.Amount);

        // Validar sobregiro según tipo de cuenta
        ValidateBalanceChange(newBalance, transaction);

        CurrentBalance = newBalance;
        
        // Por defecto, el disponible sigue al actual
        // (en casos reales, puede haber retenciones)
        AvailableBalance = newBalance;
        
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Aplica una retención al saldo disponible.
    /// </summary>
    public void ApplyHold(Money holdAmount)
    {
        if (!holdAmount.Currency.Equals(Currency))
            throw new DomainException("La moneda de la retención no coincide con la cuenta.");

        if (holdAmount.IsNegative)
            throw new DomainException("El monto de retención debe ser positivo.");

        var newAvailable = AvailableBalance.Subtract(holdAmount);
        
        if (newAvailable.IsNegative && Type != AccountType.Credit)
            throw new InsufficientFundsException(Id, holdAmount.Amount, AvailableBalance.Amount);

        AvailableBalance = newAvailable;
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Libera una retención del saldo disponible.
    /// </summary>
    public void ReleaseHold(Money holdAmount)
    {
        if (!holdAmount.Currency.Equals(Currency))
            throw new DomainException("La moneda de la retención no coincide con la cuenta.");

        AvailableBalance = AvailableBalance.Add(holdAmount.Abs());
        
        // El disponible no puede exceder el actual (excepto en crédito)
        if (Type != AccountType.Credit && AvailableBalance.IsGreaterThan(CurrentBalance))
        {
            AvailableBalance = CurrentBalance;
        }
        
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Ajusta el saldo de la cuenta (para conciliación).
    /// </summary>
    public void AdjustBalance(Money newBalance, string reason)
    {
        if (!newBalance.Currency.Equals(Currency))
            throw new DomainException("La moneda del ajuste no coincide con la cuenta.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Se requiere una razón para el ajuste de saldo.");

        CurrentBalance = newBalance;
        AvailableBalance = newBalance;
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    #endregion

    #region Account Management

    /// <summary>
    /// Desactiva la cuenta.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
            return;

        if (!CurrentBalance.IsZero)
            throw new DomainException(
                $"No se puede desactivar una cuenta con saldo pendiente: {CurrentBalance}");

        IsActive = false;
        ClosedAt = DateOnly.FromDateTime(DateTime.Today);
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Reactiva la cuenta.
    /// </summary>
    public void Reactivate()
    {
        if (IsActive)
            return;

        IsActive = true;
        ClosedAt = null;
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Actualiza el nombre de la cuenta.
    /// </summary>
    public void UpdateName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new DomainException("El nombre de la cuenta no puede estar vacío.");

        AccountName = newName.Trim();
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    #endregion

    #region Validation

    private static void ValidateAccountNumber(string accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new DomainException("El número de cuenta es requerido.");

        if (accountNumber.Length > 34)
            throw new DomainException("El número de cuenta no puede exceder 34 caracteres.");

        // Validar que solo contenga caracteres válidos
        if (!accountNumber.All(c => char.IsLetterOrDigit(c) || c == '-'))
            throw new DomainException("El número de cuenta contiene caracteres inválidos.");
    }

    private void ValidateBalanceChange(Money newBalance, Transaction transaction)
    {
        // Las cuentas de crédito pueden tener saldo negativo
        if (Type == AccountType.Credit || Type == AccountType.Loan)
            return;

        // Las demás cuentas no pueden quedar en negativo
        if (newBalance.IsNegative)
        {
            throw new InsufficientFundsException(
                Id,
                transaction.Amount.Amount,
                CurrentBalance.Amount);
        }
    }

    #endregion
}
