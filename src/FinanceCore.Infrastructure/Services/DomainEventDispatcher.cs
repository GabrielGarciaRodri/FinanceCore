using MediatR;
using Microsoft.Extensions.Logging;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Events;

namespace FinanceCore.Infrastructure.Services;

/// <summary>
/// Dispatches domain events collected by aggregate roots via MediatR.
/// Called by SaveChangesAsync in the DbContext to ensure events are published
/// within the same unit of work.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchEventsAsync(IEnumerable<BaseEntity> entities, CancellationToken cancellationToken = default);
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

    public async Task DispatchEventsAsync(IEnumerable<BaseEntity> entities, CancellationToken cancellationToken)
    {
        var entitiesWithEvents = entities
            .Where(e => e.DomainEvents.Any())
            .ToList();

        var domainEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        // Clear events before dispatching to avoid re-publishing on retry
        foreach (var entity in entitiesWithEvents)
        {
            entity.ClearDomainEvents();
        }

        foreach (var domainEvent in domainEvents)
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
                // Don't rethrow — domain event handlers should not break the main transaction
            }
        }

        if (domainEvents.Count > 0)
        {
            _logger.LogInformation(
                "Dispatched {Count} domain events",
                domainEvents.Count);
        }
    }
}
