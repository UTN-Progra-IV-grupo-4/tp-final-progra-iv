using System.Net;
using System.Net.Http.Json;
using System.Text;
using Availability.Infrastructure.Booking;
using Microsoft.Extensions.Logging.Abstractions;

namespace Availability.Tests;

public class BookingSeatOccupancyQueryTests
{
    private static readonly Guid EventId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SeatId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task ValidResponse_UsesEndpointAndServiceBearer_AndReturnsOccupiedIds()
    {
        using var handler = JsonResponse(new { eventId = EventId, seatIds = new[] { SeatId } });
        using var http = CreateHttp(handler);

        var result = await CreateQuery(http).GetOccupiedSeatsAsync(EventId, default);

        Assert.True(result.IsConfirmed);
        Assert.Equal(EventId, result.EventId);
        Assert.Equal(SeatId, Assert.Single(result.SeatIds));
        Assert.Equal($"http://booking.test/prefix/api/events/{EventId:D}/occupied-seats", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer test-service-token", handler.Authorization);
    }

    [Fact]
    public async Task ValidEmptyResponse_ConfirmsNoOccupiedSeats()
    {
        using var handler = JsonResponse(new { eventId = EventId, seatIds = Array.Empty<Guid>() });
        using var http = CreateHttp(handler);
        var result = await CreateQuery(http).GetOccupiedSeatsAsync(EventId, default);
        Assert.True(result.IsConfirmed);
        Assert.Empty(result.SeatIds);
    }

    [Theory]
    [InlineData(204)]
    [InlineData(302)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Non200Response_IsUnknown(int status)
    {
        using var handler = new BookingResponseHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var http = CreateHttp(handler);
        var result = await CreateQuery(http).GetOccupiedSeatsAsync(EventId, default);
        Assert.False(result.IsConfirmed);
        Assert.Empty(result.SeatIds);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"seatIds\":[]}")]
    [InlineData("{\"eventId\":\"10000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"eventId\":\"10000000-0000-0000-0000-000000000001\",\"seatIds\":null}")]
    [InlineData("{\"eventId\":\"10000000-0000-0000-0000-000000000002\",\"seatIds\":[]}")]
    [InlineData("{\"eventId\":\"10000000-0000-0000-0000-000000000001\",\"seatIds\":[\"invalid\"]}")]
    [InlineData("{\"eventId\":\"10000000-0000-0000-0000-000000000001\",\"seatIds\":[\"00000000-0000-0000-0000-000000000000\"]}")]
    [InlineData("{\"eventId\":\"10000000-0000-0000-0000-000000000001\",\"seatIds\":[\"20000000-0000-0000-0000-000000000001\",\"20000000-0000-0000-0000-000000000001\"]}")]
    public async Task MalformedOrInconsistentResponse_IsUnknown(string json)
    {
        using var handler = new BookingResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        using var http = CreateHttp(handler);
        Assert.False((await CreateQuery(http).GetOccupiedSeatsAsync(EventId, default)).IsConfirmed);
    }

    [Fact]
    public async Task NonJsonContentType_IsUnknown()
    {
        using var handler = new BookingResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"eventId\":\"{EventId}\",\"seatIds\":[]}}", Encoding.UTF8, "text/plain")
        }));
        using var http = CreateHttp(handler);
        Assert.False((await CreateQuery(http).GetOccupiedSeatsAsync(EventId, default)).IsConfirmed);
    }

    [Fact]
    public async Task MissingServiceToken_DoesNotSendAnAnonymousRequest()
    {
        using var handler = JsonResponse(new { eventId = EventId, seatIds = Array.Empty<Guid>() });
        using var http = CreateHttp(handler, new FakeServiceTokenProvider(token: null));
        Assert.False((await CreateQuery(http).GetOccupiedSeatsAsync(EventId, default)).IsConfirmed);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task MissingBaseUrl_DoesNotSendARequest()
    {
        using var handler = JsonResponse(new { eventId = EventId, seatIds = Array.Empty<Guid>() });
        using var http = new HttpClient(handler);
        Assert.False((await CreateQuery(http).GetOccupiedSeatsAsync(EventId, default)).IsConfirmed);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionFailureOrTimeout_IsUnknown(bool timeout)
    {
        using var handler = new BookingResponseHandler((_, _) => timeout
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException())
            : Task.FromException<HttpResponseMessage>(new HttpRequestException()));
        using var http = CreateHttp(handler);
        Assert.False((await CreateQuery(http).GetOccupiedSeatsAsync(EventId, default)).IsConfirmed);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new BookingResponseHandler((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        });
        using var http = CreateHttp(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateQuery(http).GetOccupiedSeatsAsync(EventId, cancellation.Token));
    }

    private static BookingSeatOccupancyQuery CreateQuery(HttpClient client)
        => new(client, NullLogger<BookingSeatOccupancyQuery>.Instance);

    private static HttpClient CreateHttp(HttpMessageHandler handler, FakeServiceTokenProvider? tokenProvider = null)
        => BookingHttpTestFactory.CreateClient(handler, tokenProvider ?? new FakeServiceTokenProvider());

    private static BookingResponseHandler JsonResponse(object data)
        => new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(data)
        }));
}
