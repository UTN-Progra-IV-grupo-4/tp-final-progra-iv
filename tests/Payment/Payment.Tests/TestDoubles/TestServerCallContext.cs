using Grpc.Core;

namespace Payment.Tests.TestDoubles;

// ServerCallContext mínimo para llamar al servicio sin levantar Kestrel: solo expone los headers del
// pedido y el token de cancelación, que es lo único que usa PaymentGrpcService.
public sealed class TestServerCallContext : ServerCallContext
{
    private readonly Metadata requestHeaders;

    private TestServerCallContext(Metadata requestHeaders) => this.requestHeaders = requestHeaders;

    public static TestServerCallContext Create(string? correlationId = null)
    {
        var headers = new Metadata();
        if (correlationId is not null)
            headers.Add("x-correlation-id", correlationId);

        return new TestServerCallContext(headers);
    }

    protected override string MethodCore => "/payment.PaymentService/Charge";
    protected override string HostCore => "localhost";
    protected override string PeerCore => "test";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => requestHeaders;
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata ResponseTrailersCore { get; } = new();
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new(null, new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotSupportedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
