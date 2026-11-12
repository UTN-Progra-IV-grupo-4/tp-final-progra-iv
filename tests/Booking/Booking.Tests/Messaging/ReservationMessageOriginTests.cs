using Booking.Api.Messaging;
using Contracts;

namespace Booking.Tests.Messaging;

public sealed class ReservationMessageOriginTests
{
    private static Dictionary<string, object> WithHost(object? value) =>
        value is null
            ? []
            : new Dictionary<string, object> { [BrokerMessageProperties.PublisherHost] = value };

    [Fact]
    public void ReadPublisherHost_ReturnsTheHostSentByThePublisher()
    {
        Assert.Equal("DEV-PC-01", ReservationMessageOrigin.ReadPublisherHost(WithHost("DEV-PC-01")));
    }

    [Fact]
    public void ReadPublisherHost_WithoutTheProperty_ReturnsNull()
    {
        Assert.Null(ReservationMessageOrigin.ReadPublisherHost(WithHost(null)));
    }

    [Theory]
    [InlineData(42)]
    [InlineData("host with spaces")]
    [InlineData("host\nforged log line")]
    public void ReadPublisherHost_WithAValueThatIsNotAPlainHostName_ReturnsNull(object value)
    {
        Assert.Null(ReservationMessageOrigin.ReadPublisherHost(WithHost(value)));
    }

    [Fact]
    public void ReadPublisherHost_WithAnOverlongHost_ReturnsNull()
    {
        Assert.Null(ReservationMessageOrigin.ReadPublisherHost(WithHost(new string('a', 65))));
    }

    [Theory]
    [InlineData("OTHER-PC", "DEV-PC-01", true)]
    [InlineData("DEV-PC-01", "DEV-PC-01", false)]
    [InlineData("dev-pc-01", "DEV-PC-01", false)]
    public void IsPublishedFromAnotherHost_ComparesHostsIgnoringCase(string publisherHost, string consumerHost, bool expected)
    {
        Assert.Equal(expected, ReservationMessageOrigin.IsPublishedFromAnotherHost(publisherHost, consumerHost));
    }

    [Fact]
    public void IsPublishedFromAnotherHost_WithoutAKnownPublisher_IsNeverReported()
    {
        Assert.False(ReservationMessageOrigin.IsPublishedFromAnotherHost(null, "DEV-PC-01"));
    }
}
