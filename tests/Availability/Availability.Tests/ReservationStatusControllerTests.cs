using System.Net;
using Availability.Application.Abstractions;
using Availability.Application.ReservationRequests;
using Availability.Infrastructure.Persistence.Repositories;

namespace Availability.Tests;

// Real pipeline (routing, auth, controller, Razor view) against the test PostgreSQL.
[Collection(PostgreSqlCollection.Name)]
public sealed class ReservationStatusControllerTests(PostgreSqlFixture fixture)
{
    private const string PollingMarker = "window.location.replace";

    [Fact]
    public async Task Index_BookingUnavailable_Returns503WithWarningAndKeepsPolling()
    {
        var bookingStatusClient = new ConfigurableBookingStatusClient
        {
            Answer = requestId => throw new BookingUnavailableException(requestId)
        };
        var requestId = await SeedRequestAsync("user-42");
        using var factory = CreateFactory(bookingStatusClient);

        var response = await GetStatusAsync(factory, requestId, "user-42");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("no fue rechazada", body);
        Assert.Contains(PollingMarker, body);
        Assert.Contains("poll-timeout", body);
    }

    [Fact]
    public async Task Index_Anonymous_Returns401()
    {
        var bookingStatusClient = new ConfigurableBookingStatusClient();
        var requestId = await SeedRequestAsync("user-42");
        using var factory = CreateFactory(bookingStatusClient);

        var response = await GetStatusAsync(factory, requestId, userId: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(bookingStatusClient.RequestedIds);
    }

    [Fact]
    public async Task Index_RequestOfAnotherUserAndUnknownRequest_AreIndistinguishable404WithoutAskingBooking()
    {
        var bookingStatusClient = new ConfigurableBookingStatusClient();
        var requestId = await SeedRequestAsync("user-42");
        using var factory = CreateFactory(bookingStatusClient);

        var foreignResponse = await GetStatusAsync(factory, requestId, "user-99");
        var unknownResponse = await GetStatusAsync(factory, Guid.NewGuid(), "user-99");

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(foreignResponse.StatusCode, unknownResponse.StatusCode);
        Assert.Equal(string.Empty, await foreignResponse.Content.ReadAsStringAsync());
        Assert.Equal(string.Empty, await unknownResponse.Content.ReadAsStringAsync());
        Assert.Empty(bookingStatusClient.RequestedIds);
    }

    [Fact]
    public async Task Index_RequestIdIsNotAGuid_Returns404()
    {
        var bookingStatusClient = new ConfigurableBookingStatusClient();
        using var factory = CreateFactory(bookingStatusClient);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/reservas/not-a-guid/estado");
        request.Headers.Add(ReservationStatusWebFactory.UserHeader, "user-42");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(bookingStatusClient.RequestedIds);
    }

    [Fact]
    public async Task Index_Processing_RendersProcessingAndPolls()
    {
        var body = await GetBodyAsync(_ => null);

        Assert.Contains("Procesando", body);
        Assert.Contains(PollingMarker, body);
    }

    [Fact]
    public async Task Index_Retained_RendersRetainedAndPolls()
    {
        var body = await GetBodyAsync(requestId => Result(requestId, ReservationStatus.Retained));

        Assert.Contains("Retenida", body);
        Assert.Contains(PollingMarker, body);
    }

    [Fact]
    public async Task Index_Confirmed_RendersConfirmedWithoutPolling()
    {
        var body = await GetBodyAsync(requestId => Result(requestId, ReservationStatus.Confirmed));

        Assert.Contains("Confirmada", body);
        Assert.DoesNotContain(PollingMarker, body);
    }

    [Fact]
    public async Task Index_RejectedWithReason_RendersTheReasonWithoutPolling()
    {
        var body = await GetBodyAsync(requestId =>
            Result(requestId, ReservationStatus.Rejected, ReservationRejectionReason.PaymentFailed));

        Assert.Contains("Rechazada", body);
        Assert.Contains("El pago fue rechazado.", body);
        Assert.DoesNotContain(PollingMarker, body);
    }

    [Fact]
    public async Task Index_Expired_RendersExpiredWithoutPolling()
    {
        var body = await GetBodyAsync(requestId => Result(requestId, ReservationStatus.Expired));

        Assert.Contains("Vencida", body);
        Assert.DoesNotContain(PollingMarker, body);
    }

    [Fact]
    public async Task Index_Polling_IgnoresAStartTimeInTheFutureOrNotPositive()
    {
        var body = await GetBodyAsync(_ => null);

        Assert.Contains("const startedAt = raw > 0 && raw <= Date.now() ? raw : Date.now();", body);
    }

    [Fact]
    public async Task Index_PollingConfiguredBelowOneSecond_IsClampedToOneSecond()
    {
        var bookingStatusClient = new ConfigurableBookingStatusClient { Answer = _ => null };
        var requestId = await SeedRequestAsync("user-42");
        using var factory = CreateFactory(
            bookingStatusClient,
            new Dictionary<string, string>
            {
                ["ReservationStatusPolling:IntervalSeconds"] = "0",
                ["ReservationStatusPolling:MaxSeconds"] = "-5"
            });

        var response = await GetStatusAsync(factory, requestId, "user-42");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("const intervalMs = 1 * 1000;", body);
        Assert.Contains("const maxMs = 1 * 1000;", body);
    }

    private async Task<string> GetBodyAsync(Func<Guid, ReservationStatusResult?> answer)
    {
        var bookingStatusClient = new ConfigurableBookingStatusClient { Answer = answer };
        var requestId = await SeedRequestAsync("user-42");
        using var factory = CreateFactory(bookingStatusClient);

        var response = await GetStatusAsync(factory, requestId, "user-42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static ReservationStatusResult Result(
        Guid requestId,
        ReservationStatus status,
        ReservationRejectionReason? rejectionReason = null)
        => new() { RequestId = requestId, Status = status, RejectionReason = rejectionReason };

    private ReservationStatusWebFactory CreateFactory(
        ConfigurableBookingStatusClient bookingStatusClient,
        IReadOnlyDictionary<string, string>? settings = null)
        => new(fixture.ConnectionString, bookingStatusClient, settings);

    private static async Task<HttpResponseMessage> GetStatusAsync(
        ReservationStatusWebFactory factory,
        Guid requestId,
        string? userId)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/reservas/{requestId}/estado");
        if (userId is not null)
        {
            request.Headers.Add(ReservationStatusWebFactory.UserHeader, userId);
        }

        return await client.SendAsync(request);
    }

    // Stores a request through the existing handler, so the row is built exactly as in production.
    private async Task<Guid> SeedRequestAsync(string userId)
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        await using var context = fixture.CreateDbContext();
        var requestHandler = new ReservationRequestHandler(
            new ReservationRequestRepository(context),
            new RecordingMessagePublisher());
        var reservationRequest = await requestHandler.HandleAsync(
            new ReservationRequestCommand { EventId = eventId, SeatId = seatId }, userId);
        return reservationRequest.Id;
    }
}
