using Booking.Business.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Tests.Authentication;

// Development turns on service provider validation (scopes and build-time resolution), which
// Testing does not. These tests keep Booking.Api startable on a developer machine.
public class DevelopmentStartupTests
{
    private static WebApplicationFactory<Program> CreateDevelopmentFactory(bool paymentEnabled) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Booking", "Host=unused;Database=unused");
            builder.UseSetting("Authentication:Authority", "https://keycloak.example/realms/test");
            builder.UseSetting("Payment:Enabled", paymentEnabled.ToString());
            if (paymentEnabled)
            {
                builder.UseSetting("Payment:Address", "http://payment.example:8080");
                builder.UseSetting("ServiceCredentials:Authority", "https://keycloak.example/realms/test");
                builder.UseSetting("ServiceCredentials:ClientSecret", "test-only-secret");
            }
        });

    [Fact]
    public void Startup_InDevelopmentWithPaymentDisabled_Succeeds()
    {
        using var factory = CreateDevelopmentFactory(paymentEnabled: false);

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Startup_InDevelopmentWithPaymentDisabled_NeverChargesThroughTheDisabledClient()
    {
        using var factory = CreateDevelopmentFactory(paymentEnabled: false);
        using var scope = factory.Services.CreateScope();

        var chargeClient = scope.ServiceProvider.GetRequiredService<IPaymentChargeClient>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => chargeClient.ChargeAsync(
            new PaymentChargeRequest(Guid.NewGuid(), 150000, "ARS", "correlation"), CancellationToken.None));
        Assert.Contains("Payment:Enabled", exception.Message);
    }

    [Fact]
    public void Startup_InDevelopmentWithPaymentEnabled_Succeeds()
    {
        using var factory = CreateDevelopmentFactory(paymentEnabled: true);

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }
}
