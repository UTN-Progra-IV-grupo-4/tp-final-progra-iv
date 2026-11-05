using System.Net;
using System.Net.Http.Json;
using System.Text;
using Availability.Application.Abstractions;
using Availability.Infrastructure.Booking;
using Microsoft.Extensions.Logging.Abstractions;

namespace Availability.Tests;

public class BookingStatusClientTests
{
    private static readonly Guid RequestId = Guid.Parse("c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32");

    [Fact]
    public async Task ValidResponse_CallsTheContractEndpointWithTheServiceBearer()
    {
        using var handler = Json(new { requestId = RequestId, status = "Retained", rejectionReason = (string?)null });
        using var http = CreateHttp(handler);

        var result = await new BookingStatusClient(http, NullLogger<BookingStatusClient>.Instance)
            .GetStatusAsync(RequestId);

        Assert.NotNull(result);
        Assert.Equal($"http://booking.test/prefix/api/reservations/{RequestId:D}/status", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer test-service-token", handler.Authorization);
    }

    [Theory]
    [InlineData("Retained", null, ReservationStatus.Retained, null)]
    [InlineData("Confirmed", null, ReservationStatus.Confirmed, null)]
    [InlineData("Expired", null, ReservationStatus.Expired, null)]
    [InlineData("Rejected", "SeatTaken", ReservationStatus.Rejected, ReservationRejectionReason.SeatTaken)]
    [InlineData("Rejected", "PaymentFailed", ReservationStatus.Rejected, ReservationRejectionReason.PaymentFailed)]
    [InlineData("Rejected", "PaymentUnavailable", ReservationStatus.Rejected, ReservationRejectionReason.PaymentUnavailable)]
    public async Task EachBookingStatus_IsMappedToTheApplicationResult(
        string status, string? reason, ReservationStatus expectedStatus, ReservationRejectionReason? expectedReason)
    {
        using var handler = Json(new { requestId = RequestId, status, rejectionReason = reason });
        using var http = CreateHttp(handler);

        var result = await Client(http).GetStatusAsync(RequestId);

        Assert.NotNull(result);
        Assert.Equal(RequestId, result.RequestId);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedReason, result.RejectionReason);
    }

    [Fact]
    public async Task NotFoundWithProblemDetails_MeansProcessing()
    {
        using var handler = new BookingResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"title\":\"Reservation not found\",\"status\":404}", Encoding.UTF8, "application/problem+json")
        }));
        using var http = CreateHttp(handler);

        Assert.Null(await Client(http).GetStatusAsync(RequestId));
    }

    [Theory]
    [InlineData(204)]
    [InlineData(302)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)] // bare 404 (wrong path or proxy) is not "processing"
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task AnyOtherHttpStatus_IsBookingUnavailable(int status)
    {
        using var handler = new BookingResponseHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var http = CreateHttp(handler);

        var exception = await Assert.ThrowsAsync<BookingUnavailableException>(() => Client(http).GetStatusAsync(RequestId));
        Assert.Equal(RequestId, exception.RequestId);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"requestId\":\"c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32\"}")]
    // Another request id than the one asked for.
    [InlineData("{\"requestId\":\"00000000-0000-0000-0000-000000000001\",\"status\":\"Confirmed\",\"rejectionReason\":null}")]
    // Values outside the contract: unknown status, numeric status, other casing.
    [InlineData("{\"requestId\":\"c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32\",\"status\":\"Paid\",\"rejectionReason\":null}")]
    [InlineData("{\"requestId\":\"c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32\",\"status\":1,\"rejectionReason\":null}")]
    [InlineData("{\"requestId\":\"c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32\",\"status\":\"confirmed\",\"rejectionReason\":null}")]
    // Rejected needs a known reason; any other status must not carry one.
    [InlineData("{\"requestId\":\"c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32\",\"status\":\"Rejected\",\"rejectionReason\":null}")]
    [InlineData("{\"requestId\":\"c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32\",\"status\":\"Rejected\",\"rejectionReason\":\"Other\"}")]
    [InlineData("{\"requestId\":\"c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32\",\"status\":\"Confirmed\",\"rejectionReason\":\"SeatTaken\"}")]
    public async Task MalformedOrInconsistentBody_IsBookingUnavailable_NeverAFabricatedStatus(string json)
    {
        using var handler = new BookingResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        using var http = CreateHttp(handler);

        await Assert.ThrowsAsync<BookingUnavailableException>(() => Client(http).GetStatusAsync(RequestId));
    }

    [Fact]
    public async Task NonJsonContentType_IsBookingUnavailable()
    {
        using var handler = new BookingResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"requestId\":\"{RequestId}\",\"status\":\"Confirmed\",\"rejectionReason\":null}}", Encoding.UTF8, "text/html")
        }));
        using var http = CreateHttp(handler);

        await Assert.ThrowsAsync<BookingUnavailableException>(() => Client(http).GetStatusAsync(RequestId));
    }

    [Fact]
    public async Task MissingServiceToken_DoesNotSendTheRequest_AndReportsBookingUnavailable()
    {
        using var handler = Json(new { requestId = RequestId, status = "Confirmed", rejectionReason = (string?)null });
        using var http = CreateHttp(handler, tokenProvider: new FakeServiceTokenProvider(token: null));

        await Assert.ThrowsAsync<BookingUnavailableException>(() => Client(http).GetStatusAsync(RequestId));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task MissingBaseUrl_DoesNotSendARequest()
    {
        using var handler = Json(new { requestId = RequestId, status = "Confirmed", rejectionReason = (string?)null });
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<BookingUnavailableException>(() => Client(http).GetStatusAsync(RequestId));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ConnectionFailure_IsBookingUnavailable()
    {
        using var handler = new BookingResponseHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));
        using var http = CreateHttp(handler);

        await Assert.ThrowsAsync<BookingUnavailableException>(() => Client(http).GetStatusAsync(RequestId));
    }

    [Fact]
    public async Task Timeout_IsBookingUnavailable_AndDoesNotWaitForBooking()
    {
        using var handler = new BookingResponseHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = CreateHttp(handler, timeout: TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<BookingUnavailableException>(() => Client(http).GetStatusAsync(RequestId));
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated_NotReportedAsUnavailable()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new BookingResponseHandler((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        });
        using var http = CreateHttp(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Client(http).GetStatusAsync(RequestId, cancellation.Token));
    }

    private static BookingStatusClient Client(HttpClient http) => new(http, NullLogger<BookingStatusClient>.Instance);

    private static HttpClient CreateHttp(
        HttpMessageHandler handler, FakeServiceTokenProvider? tokenProvider = null, TimeSpan? timeout = null)
        => BookingHttpTestFactory.CreateClient(
            handler, tokenProvider ?? new FakeServiceTokenProvider(), timeout: timeout);

    private static BookingResponseHandler Json(object data)
        => new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(data)
        }));
}
