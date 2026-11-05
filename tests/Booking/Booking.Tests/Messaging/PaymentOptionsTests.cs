using Booking.Api.Messaging;
using Booking.Api.Payments;
using Booking.Business.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Messaging;

public sealed class PaymentOptionsTests
{
    [Fact]
    public void DisabledPayment_DoesNotRequireAnAmount()
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Payment:Enabled"] = "false",
            ["Payment:AmountCents"] = "0"
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Null(provider.GetService<IPaymentChargeClient>());
    }

    [Theory]
    [InlineData("Payment:AmountCents", "0")]
    [InlineData("Payment:Currency", "ars")]
    [InlineData("Payment:MaxAttempts", "0")]
    [InlineData("Payment:RetryDelay", "-00:00:01")]
    [InlineData("Payment:Deadline", "00:00:00")]
    [InlineData("Payment:Deadline", "00:02:00")]
    [InlineData("Payment:Address", "")]
    [InlineData("Payment:Address", "ftp://localhost")]
    public void EnabledPayment_WithInvalidOptions_FailsAtStartup(string key, string value)
    {
        var settings = ValidSettings();
        settings[key] = value;
        using var provider = CreateProvider(settings);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void EnabledPayment_WithBoundedRetries_PassesStartupValidation()
    {
        using var provider = CreateProvider(ValidSettings());

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.IsType<GrpcPaymentChargeClient>(provider.GetRequiredService<IPaymentChargeClient>());
    }

    [Fact]
    public void DisabledPayment_DoesNotRequireARegisteredClient()
    {
        var settings = ValidSettings();
        settings["Payment:Enabled"] = "false";
        using var provider = CreateProvider(settings);

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Null(provider.GetService<IPaymentChargeClient>());
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Payment:Enabled"] = "true",
        ["Payment:Address"] = "http://localhost:5183",
        ["Payment:AmountCents"] = "150000",
        ["Payment:Currency"] = "ARS",
        ["Payment:MaxAttempts"] = "3",
        ["Payment:RetryDelay"] = "00:00:01",
        ["Payment:Deadline"] = "00:00:05",
        ["ReservationProcessing:RetentionDuration"] = "00:10:00"
    };

    private static ServiceProvider CreateProvider(IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBookingMessaging(configuration);
        return services.BuildServiceProvider();
    }
}
