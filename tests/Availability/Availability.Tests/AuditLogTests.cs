using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Availability.Application.Logging;
using Availability.Application.ReservationRequests;
using Availability.Infrastructure.Booking;
using Availability.Infrastructure.Persistence.Repositories;
using Availability.Web.Controllers;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Availability.Tests;

// Audit trail of Availability (ticket #24): the chain starts here, and the request id travels as the
// correlation id towards Booking.
[Collection(PostgreSqlCollection.Name)]
public sealed class AuditLogTests(PostgreSqlFixture fixture)
{
    private static readonly Guid RequestId = Guid.Parse("c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32");

    [Fact]
    public async Task HandleAsync_LogsTheStartOfTheChainWithTheRequestIdAsCorrelationId()
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        var logger = new RecordingLogger<ReservationRequestHandler>();
        var publisher = new RecordingMessagePublisher();
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationRequestHandler(new ReservationRequestRepository(context), publisher, logger);

        var request = await handler.HandleAsync(
            new ReservationRequestCommand { EventId = eventId, SeatId = seatId }, "user-42");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(AvailabilityAuditEvents.ReservationRequestStored, entry.EventId);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("Service: Availability", entry.Message);
        Assert.Contains($"RequestId: {request.Id}", entry.Message);
        Assert.Contains($"CorrelationId: {request.Id}", entry.Message);
        Assert.Contains($"EventId: {eventId}", entry.Message);
        Assert.Contains($"SeatId: {seatId}", entry.Message);
        // The same value goes in the broker message, which is how Booking and Payment get it.
        var message = Assert.IsType<ReservationRequestMessage>(Assert.Single(publisher.PublishedMessages));
        Assert.Equal(request.Id.ToString(), message.CorrelationId);
        // The user id is personal data: it is not part of the audit line.
        Assert.DoesNotContain("user-42", entry.Message);
    }

    [Fact]
    public async Task HandleAsync_WhenTheSeatDoesNotExist_LogsNothingBecauseNoChainStarted()
    {
        await fixture.ResetAsync();
        var logger = new RecordingLogger<ReservationRequestHandler>();
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationRequestHandler(
            new ReservationRequestRepository(context), new RecordingMessagePublisher(), logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new ReservationRequestCommand { EventId = Guid.NewGuid(), SeatId = Guid.NewGuid() }, "user-42"));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateReservation_WhenPublishFails_LogsTheFailureWithTheCorrelationIdAndErrorCode()
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        var logger = new RecordingLogger<ReservationController>();
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationRequestHandler(
            new ReservationRequestRepository(context),
            new ThrowingMessagePublisher(new InvalidOperationException("broker secret-connection-detail")));
        var controller = new ReservationController(handler, logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-42")], "test"))
                }
            }
        };

        var result = await controller.CreateReservation(
            new ReservationRequestCommand { EventId = eventId, SeatId = seatId }, CancellationToken.None);

        Assert.IsType<ObjectResult>(result);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(AvailabilityAuditEvents.ReservationRequestPublishFailed, entry.EventId);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("Service: Availability", entry.Message);
        Assert.Contains("ErrorCode: PublishFailed", entry.Message);
        var requestId = entry.Message.Split("CorrelationId: ")[1].Split(';')[0];
        Assert.True(Guid.TryParse(requestId, out _));
        Assert.DoesNotContain("secret-connection-detail", entry.Message);
    }

    [Fact]
    public async Task StatusClient_SendsTheRequestIdAsCorrelationIdHeader()
    {
        using var handler = JsonHandler(new { requestId = RequestId, status = "Retained", rejectionReason = (string?)null });
        using var http = BookingHttpTestFactory.CreateClient(handler, new FakeServiceTokenProvider());

        await new BookingStatusClient(http, NullLogger<BookingStatusClient>.Instance).GetStatusAsync(RequestId);

        Assert.Equal([RequestId.ToString("D")], handler.CorrelationIds);
        // The header name is the one Booking reads and Payment receives as gRPC metadata.
        Assert.Equal("x-correlation-id", CorrelationIdPolicy.HeaderName);
    }

    [Fact]
    public async Task OccupancyQuery_DoesNotSendACorrelationIdBecauseTheSeatMapIsNotPartOfAnyReservation()
    {
        var eventId = Guid.NewGuid();
        using var handler = JsonHandler(new { eventId, seatIds = Array.Empty<Guid>() });
        using var http = BookingHttpTestFactory.CreateClient(handler, new FakeServiceTokenProvider());

        await new BookingSeatOccupancyQuery(http, NullLogger<BookingSeatOccupancyQuery>.Instance)
            .GetOccupiedSeatsAsync(eventId, default);

        Assert.Empty(handler.CorrelationIds);
    }

    private static BookingResponseHandler JsonHandler(object data) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(data)
        }));
}
