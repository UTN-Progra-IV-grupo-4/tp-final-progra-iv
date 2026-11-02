namespace Booking.Business.Payments;

public sealed class PaymentProcessingOptions
{
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "ARS";
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
