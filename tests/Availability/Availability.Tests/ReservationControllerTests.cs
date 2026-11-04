using System.Security.Claims;
using Availability.Application.Abstractions;
using Availability.Application.ReservationRequests;
using Availability.Infrastructure.Persistence;
using Availability.Infrastructure.Persistence.Repositories;
using Availability.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Availability.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReservationControllerTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task CreateReservation_PublishFails_Returns503ProblemDetails()
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        await using var context = fixture.CreateDbContext();
        var controller = CreateController(context, new ThrowingMessagePublisher(new InvalidOperationException("secret broker detail")), "user-42");

        var result = await controller.CreateReservation(
            new ReservationRequestCommand { EventId = eventId, SeatId = seatId },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.DoesNotContain("secret broker detail", problem.Detail);
    }

    [Fact]
    public async Task CreateReservation_SeatOfAnotherEvent_Returns400()
    {
        await fixture.ResetAsync();
        var (_, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        var (otherEventId, _) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        await using var context = fixture.CreateDbContext();
        var controller = CreateController(context, new RecordingMessagePublisher(), "user-42");

        var result = await controller.CreateReservation(
            new ReservationRequestCommand { EventId = otherEventId, SeatId = seatId },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateReservation_ValidRequest_Returns202WithTheRequestId()
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        var publisher = new RecordingMessagePublisher();
        await using var context = fixture.CreateDbContext();
        var controller = CreateController(context, publisher, "user-42");

        var result = await controller.CreateReservation(
            new ReservationRequestCommand { EventId = eventId, SeatId = seatId },
            CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Single(publisher.PublishedMessages);
    }

    [Fact]
    public async Task CreateReservation_PrincipalWithoutUserId_Returns401()
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        var publisher = new RecordingMessagePublisher();
        await using var context = fixture.CreateDbContext();
        var controller = CreateController(context, publisher, userId: null);

        var result = await controller.CreateReservation(
            new ReservationRequestCommand { EventId = eventId, SeatId = seatId },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(publisher.PublishedMessages);
    }

    private static ReservationController CreateController(
        AvailabilityDbContext context,
        IMessagePublisher publisher,
        string? userId)
    {
        var handler = new ReservationRequestHandler(new ReservationRequestRepository(context), publisher);
        var claims = userId is null ? [] : new[] { new Claim("sub", userId) };
        return new ReservationController(handler, NullLogger<ReservationController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, userId is null ? null : "test"))
                }
            }
        };
    }
}
