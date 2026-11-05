using Booking.Api.Messaging;
using Booking.Business.Payments;
using Booking.Tests.TestDoubles;
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
    }

    [Theory]
    [InlineData("Payment:AmountCents", "0")]
    [InlineData("Payment:Currency", "ars")]
    [InlineData("Payment:MaxAttempts", "0")]
    [InlineData("Payment:RetryDelay", "-00:00:01")]
    [InlineData("Payment:Deadline", "00:00:00")]
    [InlineData("Payment:Deadline", "00:02:00")]
    public void EnabledPayment_WithInvalidOptions_FailsAtStartup(string key, string value)
    {
        var settings = ValidSettings();
        settings[key] = value;
        using var provider = CreateProvider(settings, registerClient: true);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void EnabledPayment_WithBoundedRetries_PassesStartupValidation()
    {
        using var provider = CreateProvider(ValidSettings(), registerClient: true);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void EnabledPayment_WithoutRegisteredClient_FailsAtStartup()
    {
        using var provider = CreateProvider(ValidSettings());

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("IPaymentChargeClient", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledPayment_DoesNotRequireARegisteredClient()
    {
        var settings = ValidSettings();
        settings["Payment:Enabled"] = "false";
        using var provider = CreateProvider(settings);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Payment:Enabled"] = "true",
        ["Payment:AmountCents"] = "150000",
        ["Payment:Currency"] = "ARS",
        ["Payment:MaxAttempts"] = "3",
        ["Payment:RetryDelay"] = "00:00:01",
        ["Payment:Deadline"] = "00:00:05",
        ["ReservationProcessing:RetentionDuration"] = "00:10:00"
    };

    private static ServiceProvider CreateProvider(
        IDictionary<string, string?> values,
        bool registerClient = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        if (registerClient)
        {
            services.AddSingleton<IPaymentChargeClient, FakePaymentChargeClient>();
        }

        services.AddBookingMessaging(configuration);
        return services.BuildServiceProvider();
    }
}
