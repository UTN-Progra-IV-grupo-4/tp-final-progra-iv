using Availability.Application.Abstractions;
using Availability.Infrastructure;
using Availability.Infrastructure.Booking;
using Microsoft.Extensions.DependencyInjection;

namespace Availability.Tests;

public sealed class BookingStatusClientRegistrationTests
{
    [Fact]
    public void AddInfrastructure_FakeEnabled_RegistersTheFakeClient()
    {
        using var provider = BuildProvider(useFakeBookingClient: true);
        using var scope = provider.CreateScope();

        Assert.IsType<FakeBookingStatusClient>(scope.ServiceProvider.GetRequiredService<IBookingStatusClient>());
    }

    [Fact]
    public void AddInfrastructure_FakeNotEnabled_RegistersTheUnavailableClient()
    {
        using var provider = BuildProvider(useFakeBookingClient: false);
        using var scope = provider.CreateScope();

        Assert.IsType<UnavailableBookingStatusClient>(scope.ServiceProvider.GetRequiredService<IBookingStatusClient>());
    }

    [Fact]
    public void AddInfrastructure_DefaultArguments_DoesNotRegisterTheFakeClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure("Host=localhost");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<UnavailableBookingStatusClient>(scope.ServiceProvider.GetRequiredService<IBookingStatusClient>());
    }

    [Fact]
    public async Task UnavailableClient_GetStatus_ThrowsBookingUnavailableWithAClearReason()
    {
        var requestId = Guid.NewGuid();
        var client = new UnavailableBookingStatusClient();

        var exception = await Assert.ThrowsAsync<BookingUnavailableException>(
            () => client.GetStatusAsync(requestId));

        Assert.Equal(requestId, exception.RequestId);
        Assert.Contains("todavía no está configurado", exception.InnerException?.Message);
    }

    private static ServiceProvider BuildProvider(bool useFakeBookingClient)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure("Host=localhost", brokerProvider: null, useFakeBookingClient: useFakeBookingClient);
        return services.BuildServiceProvider();
    }
}
