namespace Availability.Application.Events;

public enum SeatAvailability
{
    Unknown,
    Available,
    Occupied
}

public record SeatAvailabilityItem(Guid Id, string Number, SeatAvailability Availability);
public record EventSeats(Guid Id, string Name, DateTimeOffset StartsAt,
    bool OccupancyConfirmed, IReadOnlyList<SeatAvailabilityItem> Seats);
