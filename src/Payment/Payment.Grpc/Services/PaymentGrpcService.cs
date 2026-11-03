using Contracts.Payment;
using Grpc.Core;
using Payment.Business;

namespace Payment.Grpc.Services;

// Presentación: traduce entre Protobuf y los tipos de Business y convierte las excepciones en códigos
// gRPC, que es lo único que Booking interpreta. No contiene reglas de negocio.
public class PaymentGrpcService(ChargeService chargeService, ILogger<PaymentGrpcService> logger)
    : PaymentService.PaymentServiceBase
{
    // Header que manda Booking (#24). gRPC exige claves de metadata en minúsculas.
    private const string CorrelationIdHeader = "x-correlation-id";

    public override async Task<ChargeResponse> Charge(ChargeRequest request, ServerCallContext context)
    {
        var reservationId = ValidateRequest(request);

        // Scope de logging: todos los logs de ChargeService llevan el CorrelationId sin que Business lo conozca.
        // Plantilla y no diccionario: la consola la imprime legible y los proveedores estructurados
        // reciben igual CorrelationId como propiedad.
        using var _ = logger.BeginScope(
            "CorrelationId:{CorrelationId}", context.RequestHeaders.GetValue(CorrelationIdHeader));

        try
        {
            var result = await chargeService.ChargeAsync(
                reservationId, request.AmountCents, request.Currency, context.CancellationToken);
            return ToResponse(result);
        }
        catch (Exception ex) when (ToStatusCode(ex) is { } code)
        {
            throw new RpcException(new Status(code, Describe(code)));
        }
    }

    // Un pedido mal formado no llega a Business: Booking lo recibe como INVALID_ARGUMENT y no reintenta.
    private static Guid ValidateRequest(ChargeRequest request)
    {
        if (!Guid.TryParse(request.ReservationId, out var reservationId) || reservationId == Guid.Empty)
            throw InvalidArgument("reservation_id debe ser un GUID válido.");

        if (request.AmountCents <= 0)
            throw InvalidArgument("amount_cents debe ser mayor que cero.");

        if (request.Currency.Length != 3 || request.Currency.Any(c => c is < 'A' or > 'Z'))
            throw InvalidArgument("currency debe ser un código ISO 4217 de tres letras mayúsculas.");

        return reservationId;
    }

    private static RpcException InvalidArgument(string detail) =>
        new(new Status(StatusCode.InvalidArgument, detail));

    private static ChargeResponse ToResponse(ChargeResult result) => new()
    {
        Status = result.Outcome == ChargeOutcome.Approved
            ? ChargeStatus.Approved
            : ChargeStatus.Declined,
        ChargeId = result.PaymentId.ToString(),
        // Los string de Protobuf no aceptan null.
        FailureReason = result.DeclineReason ?? string.Empty
    };

    // Devuelve null para dejar pasar la excepción sin traducir: gRPC responde UNKNOWN y Booking la
    // manda a la DLQ. Así queda una base caída, porque el cobro pudo aprobarse con la fila en Pending
    // y conviene revisarlo a mano antes que reintentar.
    private static StatusCode? ToStatusCode(Exception exception) => exception switch
    {
        // Falla técnica de la pasarela: el cobro quedó Failed, sin cargo, y Booking puede reintentar.
        HttpRequestException => StatusCode.Unavailable,
        // Antes que InvalidOperationException porque hereda de ella: es un bug, no un cobro en curso.
        ObjectDisposedException => StatusCode.Internal,
        // Cobro en curso (Pending): ABORTED y no UNAVAILABLE, porque Payment sí está accesible.
        InvalidOperationException => StatusCode.Aborted,
        ArgumentException => StatusCode.InvalidArgument,
        // Modo simulado mal configurado: reintentar no lo arregla.
        NotSupportedException => StatusCode.Internal,
        _ => null
    };

    // Textos genéricos: el repositorio es público y un error interno no debe filtrar detalles.
    private static string Describe(StatusCode code) => code switch
    {
        StatusCode.Aborted => "Ya hay un cobro en curso para esta reserva.",
        StatusCode.Unavailable => "La pasarela de pago no está disponible.",
        StatusCode.InvalidArgument => "El pedido no coincide con el cobro registrado para esta reserva.",
        _ => "Error interno de Payment."
    };
}
