using System.Collections.ObjectModel;

namespace FinanceCore.Application.Common.Models;

/// <summary>
/// Representa el resultado de una operación que puede fallar.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, IEnumerable<string>? errors = null)
    {
        IsSuccess = isSuccess;
        Errors = errors != null 
            ? new ReadOnlyCollection<string>(errors.ToList()) 
            : new ReadOnlyCollection<string>(new List<string>());
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public ReadOnlyCollection<string> Errors { get; }
    
    public string? Error => Errors.FirstOrDefault();

    public static Result Success() => new(true);
    
    public static Result Failure(string error) => new(false, new[] { error });
    
    public static Result Failure(IEnumerable<string> errors) => new(false, errors);
}

/// <summary>
/// Representa el resultado de una operación que puede fallar y devuelve un valor.
/// </summary>
public class Result<T> : Result
{
    private readonly T? _value;

    protected Result(T value) : base(true)
    {
        _value = value;
    }

    protected Result(IEnumerable<string> errors) : base(false, errors)
    {
        _value = default;
    }

    public T Value
    {
        get
        {
            if (IsFailure)
                throw new InvalidOperationException("No se puede acceder al valor de un resultado fallido.");
            return _value!;
        }
    }

    public static Result<T> Success(T value) => new(value);
    
    public new static Result<T> Failure(string error) => new(new[] { error });
    
    public new static Result<T> Failure(IEnumerable<string> errors) => new(errors);

    /// <summary>
    /// Transforma el valor si el resultado es exitoso.
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        return IsSuccess 
            ? Result<TNew>.Success(mapper(Value)) 
            : Result<TNew>.Failure(Errors);
    }

    /// <summary>
    /// Encadena operaciones que pueden fallar.
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> binder)
    {
        return IsSuccess ? binder(Value) : Result<TNew>.Failure(Errors);
    }

    /// <summary>
    /// Ejecuta una acción si el resultado es exitoso.
    /// </summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess)
            action(Value);
        return this;
    }

    /// <summary>
    /// Ejecuta una acción si el resultado es fallido.
    /// </summary>
    public Result<T> OnFailure(Action<IEnumerable<string>> action)
    {
        if (IsFailure)
            action(Errors);
        return this;
    }

    /// <summary>
    /// Conversión implícita desde valor a Result exitoso.
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);
}
