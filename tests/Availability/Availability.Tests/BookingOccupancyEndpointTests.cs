using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Availability.Application.Events;
using Availability.Infrastructure;
using Availability.Infrastructure.Booking;
using Availability.Infrastructure.Events;
using Availability.Web.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Availability.Tests;

// El visitante es anónimo; el adaptador de Booking usa una identidad de servicio separada.
public sealed class BookingOccupancyEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(200, 1, true)]
    [InlineData(401, 20, true)]
    [InlineData(403, 20, true)]
    [InlineData(503, 20, true)]
    [InlineData(200, 20, false)]
    public async Task PublicMap_UsesBookingResponse_AndDisablesUnknownSeats(int status, int disabledSeats, bool hasToken)
    {
        var eventId = DemoCatalogData.EventId(1);
        using var handler = new BookingResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = JsonContent.Create(new { eventId, seatIds = new[] { DemoCatalogData.SeatId(1, 2) } })
        }));
        using var testFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Se registra explícitamente el modo real para aislar el test de
                // appsettings.Development y de las variables de la máquina del autor.
                services.RemoveAll<ISeatOccupancyQuery>();
                services.AddEventQueries(false, new BookingQueryOptions { BaseUrl = "http://booking.test/" });
                services.RemoveAll<EventDisplayOptions>();
                services.AddSingleton(new EventDisplayOptions(false));
                // Se aísla el catálogo; sí se ejecutan el cliente HTTP y el pipeline MVC reales.
                services.RemoveAll<IEventCatalog>();
                services.AddScoped<IEventCatalog, InMemoryEventCatalog>();
                services.RemoveAll<IBookingServiceTokenProvider>();
                if (hasToken)
                {
                    services.AddScoped<IBookingServiceTokenProvider, FixedBookingTokenProvider>();
                }
                else
                {
                    services.AddScoped<IBookingServiceTokenProvider, PendingBookingServiceTokenProvider>();
                }
                services.AddHttpClient<ISeatOccupancyQuery, BookingSeatOccupancyQuery>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });
        using var client = testFactory.CreateClient();
        var response = await client.GetAsync($"/eventos/{eventId:D}/asientos");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20, Regex.Matches(html, "type=\"radio\"").Count);
        Assert.Equal(disabledSeats, Regex.Matches(html, "<input[^>]+disabled=\"disabled\"[^>]*>").Count);
        Assert.Equal(hasToken ? "Bearer test-service-token" : null, handler.Authorization);
        Assert.Equal(hasToken ? 1 : 0, handler.Calls);
        Assert.DoesNotContain("Modo demostración", html);
        Assert.Equal(status != 200 || !hasToken, html.Contains("No podemos confirmar la disponibilidad"));
    }
}
