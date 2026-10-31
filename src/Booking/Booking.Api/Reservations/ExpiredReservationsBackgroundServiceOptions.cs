namespace Booking.Api.Reservations;

public sealed class ExpiredReservationsBackgroundServiceOptions
{
    public const string SectionName = "ExpiredReservations";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}
