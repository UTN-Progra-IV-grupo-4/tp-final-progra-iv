namespace Booking.Api.Payments;

internal sealed class PaymentGrpcOptions
{
    public string Address { get; set; } = string.Empty;

    public bool HasValidAddress() =>
        Uri.TryCreate(Address, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
