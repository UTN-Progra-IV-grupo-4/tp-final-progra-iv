namespace Booking.Business.Payments;

public sealed class PaymentProcessingOptions
{
    public bool Enabled { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "ARS";
    // Cantidad total de intentos de cobro (no de reintentos) ante fallos transitorios de Payment.
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    // Plazo gRPC de cada intento.
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(5);

    // Los reintentos en el peor caso (intentos por plazo más pausas) tienen que caber en menos de la
    // mitad de la retención: así el cobro termina mientras la reserva sigue Retenida y queda margen
    // para persistir el resultado antes de que venza.
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
