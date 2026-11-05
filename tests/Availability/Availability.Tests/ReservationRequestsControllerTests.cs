using System.Net;
using System.Text.RegularExpressions;
using Availability.Application.Abstractions;
using Availability.Application.ReservationRequests;
using Availability.Infrastructure.Events;
using Availability.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Availability.Tests;

// Real pipeline of the #15 screen: seat map form -> POST -> redirect -> "request received" page,
// against the test PostgreSQL. The seat map uses the demo catalog (Development), whose ids are
// loaded into the database by the demo seed.
[Collection(PostgreSqlCollection.Name)]
public sealed class ReservationRequestsControllerTests(PostgreSqlFixture fixture)
{
    private static readonly Guid DemoEventId = DemoCatalogData.EventId(1);
    private static readonly Guid DemoSeatId = DemoCatalogData.SeatId(1, 1);

    [Fact]
    public async Task SeatMap_SelectedSeat_OffersTheReserveFormWithAntiforgeryToken()
    {
        await SeedDemoDataAsync();
        using var factory = CreateFactory(new RecordingMessagePublisher());
        using var client = CreateClient(factory, "user-42");

        var body = await client.GetStringAsync(SeatMapUrl(DemoEventId, DemoSeatId));

        Assert.Contains("Reservar este asiento", body);
        Assert.Contains($"action=\"/eventos/{DemoEventId}/reservar\"", body);
        Assert.Contains($"name=\"seatId\" value=\"{DemoSeatId}\"", body);
        Assert.Contains("name=\"__RequestVerificationToken\"", body);
    }

    [Theory]
    [InlineData(null)] // Nothing selected.
    [InlineData(2)] // Occupied in the demo: not selectable.
    public async Task SeatMap_WithoutASelectableSeat_DoesNotOfferTheReserveAction(int? occupiedPosition)
    {
        await SeedDemoDataAsync();
        using var factory = CreateFactory(new RecordingMessagePublisher());
        using var client = CreateClient(factory, "user-42");
        var url = occupiedPosition is { } position
            ? SeatMapUrl(DemoEventId, DemoCatalogData.SeatId(1, position))
            : $"/eventos/{DemoEventId}/asientos";

        var body = await client.GetStringAsync(url);

        Assert.DoesNotContain("Reservar este asiento", body);
    }

    [Fact]
    public async Task Create_Anonymous_RedirectsToLoginReturningToTheSeatSelectionWithoutStoringAnything()
    {
        await SeedDemoDataAsync();
        var publisher = new RecordingMessagePublisher();
        using var factory = CreateFactory(publisher);
        using var client = CreateClient(factory, userId: null);
        var token = await GetAntiforgeryTokenAsync(client, DemoEventId, DemoSeatId);

        var response = await PostReservationAsync(client, DemoEventId, DemoSeatId.ToString(), token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("/Account/Login", location);
        // Local URL, built by the server: back to the same selection after the login.
        Assert.Contains(Uri.EscapeDataString(SeatMapUrl(DemoEventId, DemoSeatId)), location);
        Assert.Equal(0, await CountStoredRequestsAsync());
        Assert.Empty(publisher.PublishedMessages);
    }

    [Fact]
    public async Task Create_WithoutAntiforgeryToken_Returns400AndStoresNothing()
    {
        await SeedDemoDataAsync();
        var publisher = new RecordingMessagePublisher();
        using var factory = CreateFactory(publisher);
        using var client = CreateClient(factory, "user-42");

        var response = await client.PostAsync(
            $"/eventos/{DemoEventId}/reservar",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["seatId"] = DemoSeatId.ToString() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountStoredRequestsAsync());
        Assert.Empty(publisher.PublishedMessages);
    }

    [Fact]
    public async Task Create_Authenticated_StoresOneRequestPublishesOnceAndRedirectsToTheReceivedPage()
    {
        await SeedDemoDataAsync();
        var publisher = new RecordingMessagePublisher();
        using var factory = CreateFactory(publisher);
        using var client = CreateClient(factory, "user-42");
        var token = await GetAntiforgeryTokenAsync(client, DemoEventId, DemoSeatId);

        var response = await PostReservationAsync(client, DemoEventId, DemoSeatId.ToString(), token);

        // Post/Redirect/Get: a refresh of the next page cannot send the request again.
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        await using var context = fixture.CreateDbContext();
        var stored = await context.ReservationRequests.SingleAsync();
        Assert.Equal("user-42", stored.UserId);
        Assert.Equal(DemoSeatId, stored.SeatId);
        Assert.Single(publisher.PublishedMessages);
        Assert.Equal($"/reservas/{stored.Id}/recibida", response.Headers.Location!.ToString());

        var received = await client.GetAsync(response.Headers.Location);
        var body = WebUtility.HtmlDecode(await received.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        Assert.Contains("Solicitud recibida", body);
        Assert.Contains(stored.Id.ToString(), body);
        Assert.Contains("Conferencia de tecnología", body);
        Assert.Contains("A1", body);
        Assert.Contains($"href=\"/reservas/{stored.Id}/estado\"", body);
        Assert.True(received.Headers.CacheControl?.NoStore);

        await client.GetAsync(response.Headers.Location);
        Assert.Equal(1, await CountStoredRequestsAsync());
        Assert.Single(publisher.PublishedMessages);
    }

    [Fact]
    public async Task Received_RequestOfAnotherUserAndUnknownRequest_AreIndistinguishable404()
    {
        await SeedDemoDataAsync();
        var requestId = await StoreRequestAsync("user-42", DemoEventId, DemoSeatId);
        using var factory = CreateFactory(new RecordingMessagePublisher());
        using var client = CreateClient(factory, "user-99");

        var foreign = await client.GetAsync($"/reservas/{requestId}/recibida");
        var unknown = await client.GetAsync($"/reservas/{Guid.NewGuid()}/recibida");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(foreign.StatusCode, unknown.StatusCode);
        Assert.Equal(string.Empty, await foreign.Content.ReadAsStringAsync());
        Assert.Equal(string.Empty, await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Received_RequestIdIsNotAGuid_Returns404()
    {
        using var factory = CreateFactory(new RecordingMessagePublisher());
        using var client = CreateClient(factory, "user-42");

        var response = await client.GetAsync("/reservas/not-a-guid/recibida");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Received_Anonymous_RedirectsToLogin()
    {
        await SeedDemoDataAsync();
        var requestId = await StoreRequestAsync("user-42", DemoEventId, DemoSeatId);
        using var factory = CreateFactory(new RecordingMessagePublisher());
        using var client = CreateClient(factory, userId: null);

        var response = await client.GetAsync($"/reservas/{requestId}/recibida");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Received_EncodesEventAndSeatNames()
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(
            fixture, "<script>alert('event')</script>", "<b>1</b>");
        var requestId = await StoreRequestAsync("user-42", eventId, seatId);
        using var factory = CreateFactory(new RecordingMessagePublisher());
        using var client = CreateClient(factory, "user-42");

        var body = await client.GetStringAsync($"/reservas/{requestId}/recibida");

        Assert.DoesNotContain("<script>alert('event')</script>", body);
        Assert.DoesNotContain("<b>1</b>", body);
        Assert.Contains("&lt;script&gt;alert(&#x27;event&#x27;)&lt;/script&gt;", body);
        Assert.Contains("&lt;b&gt;1&lt;/b&gt;", body);
    }

    [Theory]
    [InlineData("seat-of-another-event")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public async Task Create_InvalidSeat_StoresNothingAndGoesBackToTheSeatMapWithAMessage(string seatId)
    {
        await SeedDemoDataAsync();
        var publisher = new RecordingMessagePublisher();
        using var factory = CreateFactory(publisher);
        using var client = CreateClient(factory, "user-42");
        var token = await GetAntiforgeryTokenAsync(client, DemoEventId, DemoSeatId);
        var postedSeatId = seatId == "seat-of-another-event" ? DemoCatalogData.SeatId(2, 1).ToString() : seatId;

        var response = await PostReservationAsync(client, DemoEventId, postedSeatId, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/eventos/{DemoEventId}/asientos", response.Headers.Location!.ToString());
        Assert.Equal(0, await CountStoredRequestsAsync());
        Assert.Empty(publisher.PublishedMessages);
        var body = WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location));
        Assert.Contains("ya no está disponible para reservar", body);
    }

    [Fact]
    public async Task Create_UnknownEvent_StoresNothing()
    {
        await SeedDemoDataAsync();
        var publisher = new RecordingMessagePublisher();
        using var factory = CreateFactory(publisher);
        using var client = CreateClient(factory, "user-42");
        var token = await GetAntiforgeryTokenAsync(client, DemoEventId, DemoSeatId);

        var response = await PostReservationAsync(client, Guid.NewGuid(), DemoSeatId.ToString(), token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, await CountStoredRequestsAsync());
        Assert.Empty(publisher.PublishedMessages);
    }

    [Fact]
    public async Task Create_PublishFails_Shows503FriendlyPageAndRemovesTheStoredRequest()
    {
        await SeedDemoDataAsync();
        using var factory = CreateFactory(new ThrowingMessagePublisher(new InvalidOperationException("secret broker detail")));
        using var client = CreateClient(factory, "user-42");
        var token = await GetAntiforgeryTokenAsync(client, DemoEventId, DemoSeatId);

        var response = await PostReservationAsync(client, DemoEventId, DemoSeatId.ToString(), token);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("No pudimos enviar tu solicitud, intentá de nuevo", body);
        Assert.DoesNotContain("secret broker detail", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.Contains(SeatMapUrl(DemoEventId, DemoSeatId), body);
        Assert.Equal(0, await CountStoredRequestsAsync());
    }

    private static string SeatMapUrl(Guid eventId, Guid seatId) => $"/eventos/{eventId}/asientos?selectedSeatId={seatId}";

    private ReservationStatusWebFactory CreateFactory(IMessagePublisher publisher)
        => new(fixture.ConnectionString, new ConfigurableBookingStatusClient(), messagePublisher: publisher);

    // No automatic redirects: the redirect itself is what is asserted. Cookies (anti-forgery) are kept.
    private static HttpClient CreateClient(ReservationStatusWebFactory factory, string? userId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Add(ReservationStatusWebFactory.UserHeader, userId);
        }

        return client;
    }

    // Reads the token (and keeps its cookie in the client) from the real seat map form. Anonymous
    // visitors get the form too, so they can submit it and be sent to the login.
    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, Guid eventId, Guid seatId)
    {
        var page = await client.GetStringAsync(SeatMapUrl(eventId, seatId));
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The seat map must render an anti-forgery token next to the reserve action.");
        return match.Groups[1].Value;
    }

    private static Task<HttpResponseMessage> PostReservationAsync(HttpClient client, Guid eventId, string seatId, string token)
        => client.PostAsync(
            $"/eventos/{eventId}/reservar",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["seatId"] = seatId,
                ["__RequestVerificationToken"] = token
            }));

    private async Task SeedDemoDataAsync()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "demo-data.sql"));
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<int> CountStoredRequestsAsync()
    {
        await using var context = fixture.CreateDbContext();
        return await context.ReservationRequests.CountAsync();
    }

    // Stores a request through the real handler, so the row is built exactly as in production.
    private async Task<Guid> StoreRequestAsync(string userId, Guid eventId, Guid seatId)
    {
        await using var context = fixture.CreateDbContext();
        var requestHandler = new ReservationRequestHandler(
            new ReservationRequestRepository(context),
            new RecordingMessagePublisher());
        var reservationRequest = await requestHandler.HandleAsync(
            new ReservationRequestCommand { EventId = eventId, SeatId = seatId }, userId);
        return reservationRequest.Id;
    }
}
