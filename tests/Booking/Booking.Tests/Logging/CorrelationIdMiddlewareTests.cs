using System.Net;
using System.Net.Http.Headers;
using Booking.Data.Models;
using Booking.Tests.Authentication;
using Booking.Tests.TestDoubles;
using Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Booking.Tests.Logging;

// The real Booking pipeline, with Availability's x-correlation-id header (ticket #24).
public sealed class CorrelationIdMiddlewareTests : IClassFixture<BookingApiFactory>, IDisposable
{
    private static readonly Guid ReservationId = Guid.Parse("c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32");
    private const string CorrelationId = "c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32";

    private readonly RecordingLogs logs = new();
    private readonly WebApplicationFactory<Program> factory;

    public CorrelationIdMiddlewareTests(BookingApiFactory baseFactory)
    {
        baseFactory.Repository.ReservationsById[ReservationId] = new Reservation
        {
            Id = ReservationId,
            EventId = Guid.NewGuid(),
            SeatId = Guid.NewGuid(),
            Status = ReservationStatus.Confirmed
        };
        factory = baseFactory.WithWebHostBuilder(builder =>
        {
            // The configured Default level (Information) would drop the Debug request line.
            builder.UseSetting("Logging:LogLevel:Default", "Debug");
            builder.ConfigureLogging(logging => logging.AddProvider(logs.Provider));
        });
    }

    public void Dispose() => logs.Dispose();

    [Fact]
    public async Task Get_WithValidCorrelationId_ScopesTheLogsAndEchoesTheHeader()
    {
        var response = await SendAsync(CorrelationId, withToken: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CorrelationId, Assert.Single(response.Headers.GetValues(CorrelationIdPolicy.HeaderName)));
        var entry = Assert.Single(logs.Entries, entry => entry.EventId.Name == "HttpRequestReceived");
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Contains($"Service:Booking CorrelationId:{CorrelationId}", entry.Scopes);
    }

    [Fact]
    public async Task Get_WithoutToken_StillScopesTheLogsBecauseItRunsBeforeAuthentication()
    {
        var response = await SendAsync(CorrelationId, withToken: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var entry = Assert.Single(logs.Entries, entry => entry.EventId.Name == "HttpRequestReceived");
        Assert.Contains($"Service:Booking CorrelationId:{CorrelationId}", entry.Scopes);
    }

    [Fact]
    public async Task Get_WithoutCorrelationId_DoesNotInventOneNorEchoIt()
    {
        var response = await SendAsync(correlationId: null, withToken: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(CorrelationIdPolicy.HeaderName));
        var entry = Assert.Single(logs.Entries, entry => entry.EventId.Name == "HttpRequestReceived");
        Assert.Contains("Service:Booking CorrelationId:(null)", entry.Scopes);
    }

    [Theory]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")] // 65 characters
    [InlineData("with space")]
    [InlineData("<script>")]
    [InlineData("forged;warn: Booking fake entry")]
    public async Task Get_WithUnsafeCorrelationId_ReplacesItAndStillAnswersTheRequest(string correlationId)
    {
        var response = await SendAsync(correlationId, withToken: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(CorrelationIdPolicy.HeaderName));
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain(correlationId, entry.Message, StringComparison.Ordinal);
            Assert.All(entry.Scopes, scope => Assert.DoesNotContain(correlationId, scope, StringComparison.Ordinal));
        });
        Assert.Contains(logs.Entries, entry =>
            entry.Scopes.Contains("Service:Booking CorrelationId:invalido"));
    }

    private async Task<HttpResponseMessage> SendAsync(string? correlationId, bool withToken)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/reservations/{ReservationId}/status");
        if (withToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create());
        }

        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdPolicy.HeaderName, correlationId);
        }

        return await client.SendAsync(request);
    }
}
