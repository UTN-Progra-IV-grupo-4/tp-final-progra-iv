using Availability.Application.Abstractions;
using Availability.Application.Events;
using Availability.Infrastructure.Booking;
using Availability.Infrastructure.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Availability.Tests;

// What the real host (Program.cs) wires, depending on the environment and Booking:BaseUrl.
public sealed class BookingClientHostConfigurationTests
{
    [Fact]
    public void Production_WithBaseUrl_UsesTheRealClientsForOccupancyAndStatus()
    {
        using var factory = CreateFactory("Production", source: "Database", baseUrl: "http://booking.test");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<BookingStatusClient>(scope.ServiceProvider.GetRequiredService<IBookingStatusClient>());
        Assert.IsType<BookingSeatOccupancyQuery>(scope.ServiceProvider.GetRequiredService<ISeatOccupancyQuery>());
    }

    [Fact]
    public void Production_WithoutBaseUrl_ReportsTheStatusAsUnavailable()
    {
        using var factory = CreateFactory("Production", source: "Database", baseUrl: "");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<UnavailableBookingStatusClient>(scope.ServiceProvider.GetRequiredService<IBookingStatusClient>());
    }

    [Fact]
    public void Development_WithoutBaseUrl_UsesTheFakeStatusClient()
    {
        using var factory = CreateFactory("Development", source: "Database", baseUrl: "");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<FakeBookingStatusClient>(scope.ServiceProvider.GetRequiredService<IBookingStatusClient>());
    }

    [Fact]
    public void Development_WithBaseUrl_PrefersTheRealStatusClient()
    {
        using var factory = CreateFactory("Development", source: "Database", baseUrl: "http://booking.test");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<BookingStatusClient>(scope.ServiceProvider.GetRequiredService<IBookingStatusClient>());
    }

    [Fact]
    public void DemoSource_NeverUsesTheRealBookingClients_EvenWithBaseUrl()
    {
        using var factory = CreateFactory("Development", source: "Demo", baseUrl: "http://booking.test");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<FakeBookingStatusClient>(scope.ServiceProvider.GetRequiredService<IBookingStatusClient>());
        Assert.IsType<DemoSeatOccupancyQuery>(scope.ServiceProvider.GetRequiredService<ISeatOccupancyQuery>());
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment, string source, string baseUrl)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            // Explicit values isolate the test from appsettings.Development and the author's machine.
            builder.UseSetting("EventQueries:Source", source);
            builder.UseSetting("Booking:BaseUrl", baseUrl);
            builder.UseSetting("DevelopmentAuthentication:Enabled", "false");
        });
}
