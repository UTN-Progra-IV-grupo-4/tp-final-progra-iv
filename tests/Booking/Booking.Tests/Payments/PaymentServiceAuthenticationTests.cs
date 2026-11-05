using Booking.Api.Messaging;
using Booking.Business.Payments;
using Contracts.Payment;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Tests.Payments;

// Payment runs as a separate process with authentication on. Its database is never reachable
// (nothing listens on port 1), so a call that passes authentication fails on persistence instead.
public sealed class PaymentServiceAuthenticationTests
{
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1;Pooling=false";

    [Fact]
    public async Task Charge_WithoutToken_IsUnauthenticated()
    {
        await using var keycloak = await FakeKeycloak.StartAsync();
        await using var payment = await PaymentProcess.StartAsync(UnreachableDatabase, keycloak.Authority);

        var exception = await Assert.ThrowsAsync<RpcException>(() => ChargeAsync(payment, token: null));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task Charge_WithTokenOfAnotherClient_IsPermissionDenied()
    {
        await using var keycloak = await FakeKeycloak.StartAsync();
        await using var payment = await PaymentProcess.StartAsync(UnreachableDatabase, keycloak.Authority);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            ChargeAsync(payment, keycloak.CreateToken("availability-service")));

        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
    }

    [Fact]
    public async Task Adapter_WithClientCredentials_PassesPaymentAuthentication()
    {
        await using var keycloak = await FakeKeycloak.StartAsync();
        await using var payment = await PaymentProcess.StartAsync(UnreachableDatabase, keycloak.Authority);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:Enabled"] = "true",
                ["Payment:Address"] = payment.Address,
                ["Payment:AmountCents"] = "150000",
                ["Payment:Currency"] = "ARS",
                ["ReservationProcessing:RetentionDuration"] = "00:10:00",
                ["ServiceCredentials:Authority"] = keycloak.Authority,
                ["ServiceCredentials:ClientId"] = "booking-service",
                ["ServiceCredentials:ClientSecret"] = "local-test-secret",
                ["ServiceCredentials:RequireHttpsMetadata"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBookingMessaging(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IPaymentChargeClient>();

        var exception = await Assert.ThrowsAsync<PaymentChargeException>(() =>
            client.ChargeAsync(new PaymentChargeRequest(Guid.NewGuid(), 150000, "ARS", "correlation-19")));

        // A rejected credential would be UNAUTHENTICATED or PERMISSION_DENIED, both UnexpectedResponse.
        // The request reached Payment's service and failed on the unreachable database instead.
        Assert.NotEqual(PaymentChargeFailure.UnexpectedResponse, exception.Failure);
        Assert.Equal(1, keycloak.TokenRequestCount);
    }

    private static async Task<ChargeResponse> ChargeAsync(PaymentProcess payment, string? token)
    {
        using var channel = GrpcChannel.ForAddress(payment.Address);
        var client = new PaymentService.PaymentServiceClient(channel);
        var headers = new Metadata();
        if (token is not null)
        {
            headers.Add("Authorization", $"Bearer {token}");
        }

        return await client.ChargeAsync(
            new ChargeRequest { ReservationId = Guid.NewGuid().ToString("D"), AmountCents = 150000, Currency = "ARS" },
            headers,
            deadline: DateTime.UtcNow.AddSeconds(10));
    }
}
