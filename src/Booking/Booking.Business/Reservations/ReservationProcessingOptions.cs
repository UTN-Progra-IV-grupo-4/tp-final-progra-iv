namespace Booking.Business.Reservations;

public sealed class ReservationProcessingOptions
{
    public const string SectionName = "ReservationProcessing";

    public TimeSpan RetentionDuration { get; set; }

    public TimeSpan ExpirationCheckInterval { get; set; }

    public int ExpirationBatchSize { get; set; }
}
