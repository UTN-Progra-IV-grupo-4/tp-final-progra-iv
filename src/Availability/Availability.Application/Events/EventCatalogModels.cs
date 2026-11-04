namespace Availability.Application.Events;

// Son datos de consulta: Web no necesita recibir entidades seguidas por EF Core.
public record EventSummary(Guid Id, string Name, DateTimeOffset StartsAt, int SeatCount);
public record CatalogSeat(Guid Id, string Number);
public record CatalogEvent(Guid Id, string Name, DateTimeOffset StartsAt, IReadOnlyList<CatalogSeat> Seats);
