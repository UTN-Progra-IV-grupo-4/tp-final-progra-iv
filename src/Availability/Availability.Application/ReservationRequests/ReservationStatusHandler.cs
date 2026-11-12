using Availability.Application.Abstractions;
using Availability.Application.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Availability.Application.ReservationRequests;

// The logger and the time provider are optional so callers that only care about the use case need
// not build them; the dependency injection container supplies the logger.
public class ReservationStatusHandler(
    IReservationRequestRepository repository,
    IBookingStatusClient bookingStatusClient,
    ILogger<ReservationStatusHandler>? logger = null,
    TimeProvider? timeProvider = null)
{
    // A confirmed reservation takes a few seconds; past this, Booking not knowing the request is a symptom.
    public static readonly TimeSpan NotRegisteredWarningAfter = TimeSpan.FromSeconds(15);

    // The status page polls every few seconds. Warning only inside this window after the threshold keeps it
    // to a couple of warnings per open page (the window holds two polls at the default 3 s) instead of one
    // on every poll for the whole two minutes. It assumes the polling interval is shorter than the window
    // (ReservationStatusPolling:IntervalSeconds): with a longer interval a poll may miss the window and the
    // warning would not appear. Each open page polling the same request warns on its own.
    public static readonly TimeSpan NotRegisteredWarningWindow = TimeSpan.FromSeconds(6);

    private readonly ILogger<ReservationStatusHandler> _logger =
        logger ?? NullLogger<ReservationStatusHandler>.Instance;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    // Returns null while Booking has not consumed the request yet ("processing").
    // Throws KeyNotFoundException when the request does not exist or belongs to another user.
    // Throws BookingUnavailableException when Booking cannot answer.
    public async Task<ReservationStatusResult?> HandleAsync(
        Guid requestId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var reservationRequest = await repository.GetByIdAsync(requestId, cancellationToken);
        // Booking does not check ownership, so it is checked here before asking Booking.
        // Missing and foreign requests get the same answer: other users' ids are not revealed.
        if (reservationRequest is null || reservationRequest.UserId != userId)
        {
            throw new KeyNotFoundException($"Reservation request {requestId} was not found.");
        }

        var status = await bookingStatusClient.GetStatusAsync(requestId, cancellationToken);
        if (status is null)
        {
            WarnIfBookingIsTakingTooLong(requestId, reservationRequest.RequestedAt);
        }

        return status;
    }

    private void WarnIfBookingIsTakingTooLong(Guid requestId, DateTimeOffset requestedAt)
    {
        var elapsed = _timeProvider.GetUtcNow() - requestedAt;
        if (elapsed < NotRegisteredWarningAfter || elapsed >= NotRegisteredWarningAfter + NotRegisteredWarningWindow)
        {
            return;
        }

        // The request id is also the message id and the correlation id (see ReservationRequestStored).
        _logger.LogWarning(
            AvailabilityAuditEvents.ReservationRequestNotRegistered,
            "Booking has not registered the reservation request yet. The message may be waiting in the broker, may have been consumed by another consumer of the same subscription, or Booking may be down. Service: {Service}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; ElapsedSeconds: {ElapsedSeconds}.",
            AvailabilityAuditEvents.ServiceName,
            requestId,
            requestId,
            (int)elapsed.TotalSeconds);
    }
}
