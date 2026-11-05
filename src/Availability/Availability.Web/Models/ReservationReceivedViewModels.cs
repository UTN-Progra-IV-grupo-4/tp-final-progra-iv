using Availability.Application.ReservationRequests;

namespace Availability.Web.Models;

public record ReservationReceivedViewModel(ReservationReceipt Receipt);

// Retry target for the page shown when the request could not be handed to the broker.
public record ReservationPublishFailedViewModel(Guid EventId, Guid SeatId);
