namespace Booking.Business.Payments;

public sealed class PaymentProcessingOptions
{
    public bool Enabled { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "ARS";
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(5);

    public bool IsValidFor(TimeSpan retentionDuration) =>
        !Enabled ||
        (AmountCents > 0 &&
         Currency is { Length: 3 } &&
         Currency.All(character => character is >= 'A' and <= 'Z') &&
         MaxAttempts > 0 &&
         RetryDelay >= TimeSpan.Zero &&
         Deadline > TimeSpan.Zero &&
         retentionDuration > TimeSpan.Zero &&
         (decimal)MaxAttempts * Deadline.Ticks +
         (decimal)(MaxAttempts - 1) * RetryDelay.Ticks <
         retentionDuration.Ticks / 2m);
}
