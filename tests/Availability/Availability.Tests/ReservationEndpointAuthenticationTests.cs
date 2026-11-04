using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Availability.Tests;

// Runs the real pipeline: without a valid scheme [Authorize] used to fail with a 500.
public sealed class ReservationEndpointAuthenticationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PostReservations_WithoutCredentials_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            new { eventId = Guid.NewGuid(), seatId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
