using FinanceCore.Domain.Events;

namespace FinanceCore.Domain.Entities;

/// <summary>
/// Entidad base con propiedades comunes.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Identificador único.
    /// </summary>
    public Guid Id { get; protected set; }
    
    /// <summary>
    /// Fecha de creación.
    /// </summary>
    public DateTimeOffset CreatedAt { get; protected set; }
    
    /// <summary>
    /// Fecha de última actualización.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; protected set; }

    private readonly List<IDomainEvent> _domainEvents = new();
    
    /// <summary>
    /// Eventos de dominio pendientes de publicar.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Agrega un evento de dominio.
    /// </summary>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Limpia los eventos de dominio después de publicarlos.
    /// </summary>
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

/// <summary>
/// Marcador para Aggregate Roots.
/// </summary>
public interface IAggregateRoot
{
}
