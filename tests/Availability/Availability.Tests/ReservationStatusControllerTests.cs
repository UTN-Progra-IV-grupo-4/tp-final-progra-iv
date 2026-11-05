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
