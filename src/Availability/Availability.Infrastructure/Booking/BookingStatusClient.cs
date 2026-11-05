using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Availability.Application.Abstractions;
using Contracts;
using Microsoft.Extensions.Logging;
using ServiceAuthentication;

namespace Availability.Infrastructure.Booking;

// HTTP client for GET /api/reservations/{requestId}/status (contract: docs/contracts/reservation-status.md).
// It never invents a status: anything that is not a valid 200, or a 404 Problem Details answer
// ("not consumed yet"), is reported as Booking unavailable.
public sealed class BookingStatusClient(
    HttpClient httpClient,
    ILogger<BookingStatusClient> logger) : IBookingStatusClient
{
    public async Task<ReservationStatusResult?> GetStatusAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requestId == Guid.Empty || httpClient.BaseAddress is null)
        {
            throw new BookingUnavailableException(
                requestId,
                new InvalidOperationException("Booking:BaseUrl is not configured or the request id is empty."));
        }

        // The limit covers the token acquisition, the request and the body read.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(httpClient.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/reservations/{requestId:D}/status");
            request.Headers.Accept.ParseAdd("application/json");
            // The request id is the correlation id of the whole chain: Booking logs it with this call.
            request.Headers.Add(CorrelationIdPolicy.HeaderName, requestId.ToString("D"));
            using var response = await httpClient.SendAsync(request, timeout.Token);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => await ReadStatusAsync(requestId, response, timeout.Token),
                HttpStatusCode.NotFound => ReadNotFound(requestId, response),
                // 400, 401, 403, 3xx, 5xx...: Booking could not report the status.
                _ => throw Unavailable(requestId, $"Booking answered HTTP {(int)response.StatusCode}.")
            };
        }
        catch (BookingUnavailableException)
        {
            throw;
        }
        catch (ServiceTokenAcquisitionException exception)
        {
            logger.LogWarning("No service token to ask Booking for the status of {RequestId}.", requestId);
            throw new BookingUnavailableException(requestId, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timed out asking Booking for the status of {RequestId}.", requestId);
            throw new BookingUnavailableException(requestId, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            logger.LogWarning("Could not get the status of {RequestId} from Booking: {ErrorType}.",
                requestId, exception.GetType().Name);
            throw new BookingUnavailableException(requestId, exception);
        }
    }

    // A bare 404 (wrong base path, proxy, missing route) must not be shown as "processing":
    // only Booking's Problem Details answer means "no reservation recorded yet".
    private ReservationStatusResult? ReadNotFound(Guid requestId, HttpResponseMessage response)
    {
        if (!IsMediaType(response, "application/problem+json"))
        {
            throw Unavailable(requestId, "Booking answered HTTP 404 without Problem Details.");
        }

        return null;
    }

    private async Task<ReservationStatusResult> ReadStatusAsync(
        Guid requestId, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!IsMediaType(response, "application/json"))
        {
            throw Unavailable(requestId, "Booking answered with an unexpected content type.");
        }

        var body = await response.Content.ReadFromJsonAsync<StatusResponse>(cancellationToken);
        if (body is null || body.RequestId != requestId || !TryMapStatus(body, out var result))
        {
            throw Unavailable(requestId, "Booking answered with an invalid or inconsistent status.");
        }

        return result;
    }

    private static bool TryMapStatus(StatusResponse body, out ReservationStatusResult result)
    {
        result = new ReservationStatusResult();

        // Exact contract values only: numeric strings or other casing are not accepted.
        ReservationStatus status;
        switch (body.Status)
        {
            case "Retained": status = ReservationStatus.Retained; break;
            case "Confirmed": status = ReservationStatus.Confirmed; break;
            case "Rejected": status = ReservationStatus.Rejected; break;
            case "Expired": status = ReservationStatus.Expired; break;
            default: return false;
        }

        ReservationRejectionReason? reason;
        switch (body.RejectionReason)
        {
            case null: reason = null; break;
            case "SeatTaken": reason = ReservationRejectionReason.SeatTaken; break;
            case "PaymentFailed": reason = ReservationRejectionReason.PaymentFailed; break;
            case "PaymentUnavailable": reason = ReservationRejectionReason.PaymentUnavailable; break;
            default: return false;
        }

        // The reason exists if, and only if, the reservation was rejected.
        if ((status == ReservationStatus.Rejected) != reason.HasValue)
        {
            return false;
        }

        result = new ReservationStatusResult
        {
            RequestId = body.RequestId!.Value,
            Status = status,
            RejectionReason = reason
        };
        return true;
    }

    private static bool IsMediaType(HttpResponseMessage response, string mediaType) =>
        string.Equals(response.Content.Headers.ContentType?.MediaType, mediaType, StringComparison.OrdinalIgnoreCase);

    private BookingUnavailableException Unavailable(Guid requestId, string reason)
    {
        logger.LogWarning("Booking could not report the status of {RequestId}: {Reason}", requestId, reason);
        return new BookingUnavailableException(requestId, new InvalidOperationException(reason));
    }

    // Private transport DTO: Availability does not reference Booking's internal layers.
    private sealed class StatusResponse
    {
        public Guid? RequestId { get; init; }
        public string? Status { get; init; }
        public string? RejectionReason { get; init; }
    }
}
