using MediatR;
using Microsoft.Extensions.Logging;
using FinanceCore.Domain.Events;

namespace FinanceCore.Infrastructure.Services;

/// <summary>
/// Publishes domain events captured during a unit of work AFTER the commit succeeds.
/// Events do NOT participate in the original DB transaction — they fire only when
/// the persistence has been confirmed, so a failed save never produces phantom events.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken cancellationToken = default);
}

public class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IMediator _mediator;
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(IMediator mediator, ILogger<DomainEventDispatcher> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0) return;

        foreach (var domainEvent in events)
        {
            _logger.LogDebug(
                "Dispatching domain event {EventType} (EventId: {EventId})",
                domainEvent.GetType().Name,
                domainEvent.EventId);

            try
            {
                await _mediator.Publish(domainEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error dispatching domain event {EventType} (EventId: {EventId})",
                    domainEvent.GetType().Name,
                    domainEvent.EventId);
                // El commit ya pasó; no podemos abortar la transacción.
                // Los handlers fallidos quedan registrados para reproceso manual.
            }
        }

        _logger.LogInformation("Dispatched {Count} domain events post-commit", events.Count);
    }
}
