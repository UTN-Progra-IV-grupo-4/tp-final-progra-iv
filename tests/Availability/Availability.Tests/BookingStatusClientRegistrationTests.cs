using Availability.Application.Abstractions;
using Availability.Infrastructure;
using Availability.Infrastructure.Booking;
using Microsoft.Extensions.DependencyInjection;
using ServiceAuthentication;

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddInfrastructure_BaseUrlConfigured_RegistersTheRealClient_EvenWhenTheFakeIsAllowed(bool useFakeBookingClient)
    {
        using var provider = BuildProvider(useFakeBookingClient, new BookingQueryOptions { BaseUrl = "http://booking.test" });
        using var scope = provider.CreateScope();

        Assert.IsType<BookingStatusClient>(scope.ServiceProvider.GetRequiredService<IBookingStatusClient>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddInfrastructure_BaseUrlBlank_FallsBackToTheFakeOrTheUnavailableClient(string baseUrl)
    {
        var options = new BookingQueryOptions { BaseUrl = baseUrl };
        using var development = BuildProvider(useFakeBookingClient: true, options);
        using var otherEnvironment = BuildProvider(useFakeBookingClient: false, options);

        Assert.IsType<FakeBookingStatusClient>(development.CreateScope().ServiceProvider.GetRequiredService<IBookingStatusClient>());
        Assert.IsType<UnavailableBookingStatusClient>(otherEnvironment.CreateScope().ServiceProvider.GetRequiredService<IBookingStatusClient>());
    }

    [Fact]
    public void AddInfrastructure_InvalidBaseUrl_FailsAtStartup()
    {
        Assert.Throws<InvalidOperationException>(
            () => BuildProvider(useFakeBookingClient: false, new BookingQueryOptions { BaseUrl = "ftp://booking.test" }));
    }

    [Fact]
    public async Task UnavailableClient_GetStatus_ThrowsBookingUnavailableWithAClearReason()
    {
        var requestId = Guid.NewGuid();
        var client = new UnavailableBookingStatusClient();

        var exception = await Assert.ThrowsAsync<BookingUnavailableException>(
            () => client.GetStatusAsync(requestId));

        Assert.Equal(requestId, exception.RequestId);
        Assert.Contains("Booking:BaseUrl is not configured", exception.InnerException?.Message);
    }

    private static ServiceProvider BuildProvider(bool useFakeBookingClient, BookingQueryOptions? bookingOptions = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<BearerTokenHandler>();
        services.AddSingleton<IServiceTokenProvider>(new FakeServiceTokenProvider());
        services.AddInfrastructure("Host=localhost", brokerProvider: null, useFakeBookingClient: useFakeBookingClient,
            bookingOptions: bookingOptions);
        return services.BuildServiceProvider();
    }
}
