using Availability.Application.Events;

namespace Availability.Web.Models;

public record EventDisplayOptions(bool IsDemo);
public record EventsPageViewModel(IReadOnlyList<EventSummary> Events, bool IsDemo);
public record EventSeatsViewModel(EventSeats Event, bool IsDemo, Guid? SelectedSeatId)
{
    public SeatAvailabilityItem? SelectedSeat => Event.Seats.SingleOrDefault(s => s.Id == SelectedSeatId);
}
