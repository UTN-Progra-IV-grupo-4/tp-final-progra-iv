using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServiceAuthentication;

namespace Booking.Tests.Authentication;

public class ServiceTokenWiringTests : IClassFixture<BookingApiFactory>
{
    private readonly BookingApiFactory factory;

    public ServiceTokenWiringTests(BookingApiFactory factory) => this.factory = factory;

    [Fact]
    public void Host_RegistersTheBookingServiceTokenProvider()
    {
        Assert.IsType<ClientCredentialsTokenProvider>(factory.Services.GetRequiredService<IServiceTokenProvider>());

        var options = factory.Services.GetRequiredService<IOptions<ClientCredentialsOptions>>().Value;
        Assert.Equal("booking-service", options.ClientId);
        Assert.True(options.RequireHttpsMetadata);
        Assert.Empty(options.ClientSecret);
    }
}
