using System.Net;
using System.Net.Http.Headers;
using Booking.Data.Models;

namespace Booking.Tests.Authentication;

public class ProtectedEndpointsAuthenticationTests : IClassFixture<BookingApiFactory>
{
    private static readonly Guid ReservationId = Guid.Parse("c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32");
    private static readonly Guid EventId = Guid.Parse("0d3b8d0e-5d4c-4b4b-9a38-6a1f4f6a3b11");

    public static TheoryData<string> ProtectedPaths => new()
    {
        $"/api/reservations/{ReservationId}/status",
        $"/api/events/{EventId}/occupied-seats"
    };

    private readonly BookingApiFactory factory;

    public ProtectedEndpointsAuthenticationTests(BookingApiFactory factory)
    {
        this.factory = factory;
        factory.Repository.ReservationsById[ReservationId] = new Reservation
        {
            Id = ReservationId,
            EventId = EventId,
            SeatId = Guid.NewGuid(),
            Status = ReservationStatus.Confirmed
        };
    }

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task Get_WithoutToken_ReturnsUnauthorized(string path)
    {
        var response = await SendAsync(path, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task Get_WithWrongAudience_ReturnsUnauthorized(string path)
    {
        var token = TestTokens.Create(audience: "payment-service");

        var response = await SendAsync(path, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task Get_WithWrongIssuer_ReturnsUnauthorized(string path)
    {
        var token = TestTokens.Create(issuer: "http://other.test/realms/seat-reservation");

        var response = await SendAsync(path, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task Get_WithExpiredToken_ReturnsUnauthorized(string path)
    {
        var token = TestTokens.Create(lifetime: TimeSpan.FromMinutes(-10));

        var response = await SendAsync(path, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task Get_WithWrongSignature_ReturnsUnauthorized(string path)
    {
        var token = TestTokens.Create(
            signingKey: TestTokens.CreateKey("another-signing-key-0123456789-abcdef"));

        var response = await SendAsync(path, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task Get_WithOtherServiceToken_ReturnsForbidden(string path)
    {
        var token = TestTokens.Create(authorizedParty: "payment-service");

        var response = await SendAsync(path, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task Get_WithAvailabilityServiceToken_ReturnsOk(string path)
    {
        var token = TestTokens.Create(authorizedParty: "availability-service");

        var response = await SendAsync(path, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task Get_WithoutConfiguredAuthority_ReturnsUnauthorized(string path)
    {
        await using var unconfiguredFactory = new UnconfiguredAuthorityBookingApiFactory();
        var token = TestTokens.Create();

        var response = await SendAsync(unconfiguredFactory, path, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<HttpResponseMessage> SendAsync(string path, string? token) =>
        SendAsync(factory, path, token);

    private static async Task<HttpResponseMessage> SendAsync(
        BookingApiFactory apiFactory,
        string path,
        string? token)
    {
        using var client = apiFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }
}
