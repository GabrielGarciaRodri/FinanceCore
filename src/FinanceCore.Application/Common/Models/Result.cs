using System.Text.Json.Serialization;

namespace FinanceCore.Application.Common.Models;

/// <summary>
/// Representa el resultado de una operación que puede fallar.
/// </summary>
public class Result
{
    /// <summary>
    /// Constructor JSON-friendly. Es el único que System.Text.Json puede invocar,
    /// permitiendo round-trip (necesario para el CachingBehavior que serializa/
    /// deserializa respuestas a Redis).
    /// IMPORTANTE: los nombres y tipos de los parámetros deben matchear
    /// exactamente las propiedades expuestas (IsSuccess, Errors). Por eso el
    /// parámetro errors es IReadOnlyList&lt;string&gt; (no IEnumerable) — System.Text.Json
    /// hace matching estricto de tipo, no por covarianza.
    /// </summary>
    [JsonConstructor]
    protected Result(bool isSuccess, IReadOnlyList<string>? errors = null)
    {
        IsSuccess = isSuccess;
        Errors = errors ?? Array.Empty<string>();
    }

    public bool IsSuccess { get; }

    [JsonIgnore]
    public bool IsFailure => !IsSuccess;

    public IReadOnlyList<string> Errors { get; }

    [JsonIgnore]
    public string? Error => Errors.Count > 0 ? Errors[0] : null;

    public static Result Success() => new(true);

    public static Result Failure(string error) => new(false, new[] { error });

    public static Result Failure(IEnumerable<string> errors) => new(false, errors.ToArray());
}

/// <summary>
/// Representa el resultado de una operación que puede fallar y devuelve un valor.
/// </summary>
public class Result<T> : Result
{
    /// <summary>
    /// Constructor JSON-friendly para deserialización vía System.Text.Json.
    /// </summary>
    [JsonConstructor]
    protected Result(bool isSuccess, IReadOnlyList<string>? errors, T? value)
        : base(isSuccess, errors)
    {
        Value = value;
    }

    // Constructors privados para los factory methods.
    private Result(T value) : base(true, null)
    {
        Value = value;
    }

    private Result(IReadOnlyList<string> errors) : base(false, errors)
    {
        Value = default;
    }

    /// <summary>
    /// Valor del resultado. Es null cuando <see cref="Result.IsFailure"/> es true.
    /// Para acceso defensivo que arroja excepción en estado fallido, usar
    /// <see cref="GetValueOrThrow"/>.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Obtiene el valor o arroja <see cref="InvalidOperationException"/> si el
    /// resultado es fallido. Útil cuando el caller conoce que IsSuccess pero
    /// quiere asegurar el contrato no-null.
    /// </summary>
    public T GetValueOrThrow()
    {
        if (IsFailure)
            throw new InvalidOperationException("No se puede acceder al valor de un resultado fallido.");
        return Value!;
    }

    public static Result<T> Success(T value) => new(value);

    public new static Result<T> Failure(string error) => new(new[] { error });

    public new static Result<T> Failure(IEnumerable<string> errors) => new(errors.ToArray());

    /// <summary>
    /// Transforma el valor si el resultado es exitoso.
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        return IsSuccess
            ? Result<TNew>.Success(mapper(Value!))
            : Result<TNew>.Failure(Errors);
    }

    /// <summary>
    /// Encadena operaciones que pueden fallar.
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> binder)
    {
        return IsSuccess ? binder(Value!) : Result<TNew>.Failure(Errors);
    }

    /// <summary>
    /// Ejecuta una acción si el resultado es exitoso.
    /// </summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess)
            action(Value!);
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
