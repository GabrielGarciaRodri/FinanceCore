using System.Diagnostics;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinanceCore.Application.Common.Behaviors;

/// <summary>
/// Behavior que registra entrada/salida de cada request en el pipeline.
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var requestId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogInformation(
            "[{RequestId}] Iniciando {RequestName}",
            requestId, requestName);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();

            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > 5000)
            {
                _logger.LogWarning(
                    "[{RequestId}] {RequestName} tardó {ElapsedMs}ms (LENTO)",
                    requestId, requestName, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation(
                    "[{RequestId}] {RequestName} completado en {ElapsedMs}ms",
                    requestId, requestName, stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex,
                "[{RequestId}] {RequestName} falló después de {ElapsedMs}ms. Error: {ErrorMessage}",
                requestId, requestName, stopwatch.ElapsedMilliseconds, ex.Message);

            throw;
        }
    }
}

/// <summary>
/// Behavior que ejecuta validaciones de FluentValidation antes del handler.
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Any())
        {
            _logger.LogWarning(
                "Validación fallida para {RequestName}. Errores: {Errors}",
                requestName,
                string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")));

            throw new ValidationException(failures);
        }

        return await next();
    }
}

/// <summary>
/// Behavior que envuelve el handler en una transacción de base de datos.
/// Solo aplica a requests que implementan ITransactionalRequest.
/// </summary>
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Solo aplicar a requests transaccionales
        if (request is not ITransactionalRequest)
        {
            return await next();
        }

        var requestName = typeof(TRequest).Name;

        _logger.LogDebug("Iniciando transacción para {RequestName}", requestName);

        try
        {
            // Aquí se integraría con IUnitOfWork
            // await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var response = await next();

            // await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogDebug("Transacción completada para {RequestName}", requestName);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transacción fallida para {RequestName}, ejecutando rollback", requestName);

            // await _unitOfWork.RollbackTransactionAsync(cancellationToken);

            throw;
        }
    }
}

/// <summary>
/// Behavior para cachear respuestas de queries.
/// Solo aplica a requests que implementan ICacheableQuery.
/// </summary>
public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Solo aplicar a queries cacheables
        if (request is not ICacheableQuery cacheableQuery)
        {
            return await next();
        }

        var cacheKey = cacheableQuery.CacheKey;
        var requestName = typeof(TRequest).Name;

        // Aquí se integraría con IDistributedCache
        // var cachedResponse = await _cache.GetAsync(cacheKey, cancellationToken);
        // if (cachedResponse != null)
        // {
        //     _logger.LogDebug("Cache HIT para {RequestName} con key {CacheKey}", requestName, cacheKey);
        //     return JsonSerializer.Deserialize<TResponse>(cachedResponse);
        // }

        _logger.LogDebug("Cache MISS para {RequestName} con key {CacheKey}", requestName, cacheKey);

        var response = await next();

        // await _cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(response), 
        //     new DistributedCacheEntryOptions
        //     {
        //         AbsoluteExpirationRelativeToNow = cacheableQuery.CacheDuration
        //     }, cancellationToken);

        return response;
    }
}

#region Interfaces de marcado

/// <summary>
/// Marca un request como transaccional.
/// El TransactionBehavior envolverá el handler en una transacción de BD.
/// </summary>
public interface ITransactionalRequest { }

/// <summary>
/// Marca una query como cacheable.
/// El CachingBehavior almacenará la respuesta en caché.
/// </summary>
public interface ICacheableQuery
{
    /// <summary>
    /// Clave única para el caché.
    /// </summary>
    string CacheKey { get; }

    /// <summary>
    /// Duración del caché.
    /// </summary>
    TimeSpan CacheDuration { get; }
}

#endregion
