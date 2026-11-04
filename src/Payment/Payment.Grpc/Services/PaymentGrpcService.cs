using System.Data.Common;
using Contracts.Payment;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Payment.Business;

namespace Payment.Grpc.Services;

// Presentación: traduce entre Protobuf y los tipos de Business y convierte las excepciones en códigos
// gRPC, que es lo único que Booking interpreta. No contiene reglas de negocio.
public class PaymentGrpcService(
    ChargeService chargeService,
    IOptions<ChargeOptions> options,
    ILogger<PaymentGrpcService> logger)
    : PaymentService.PaymentServiceBase
{
    // Header que manda Booking (#24). gRPC exige claves de metadata en minúsculas.
    private const string CorrelationIdHeader = "x-correlation-id";

    // Holgado para un GUID (36) o un id con prefijo; un valor más largo no es un id de correlación.
    private const int MaxCorrelationIdLength = 64;
    private const string InvalidCorrelationId = "invalido";

    // xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
    private const int GuidFormatDLength = 36;

    public override async Task<ChargeResponse> Charge(ChargeRequest request, ServerCallContext context)
    {
        var reservationId = ValidateRequest(request);

        // Scope de logging: todos los logs de ChargeService llevan el CorrelationId sin que Business lo conozca.
        // Plantilla y no diccionario: la consola la imprime legible y los proveedores estructurados
        // reciben igual CorrelationId como propiedad.
        using var _ = logger.BeginScope(
            "CorrelationId:{CorrelationId}",
            SanitizeCorrelationId(context.RequestHeaders.GetValue(CorrelationIdHeader)));

        try
        {
            var result = await chargeService.ChargeAsync(
                reservationId, request.AmountCents, request.Currency, context.CancellationToken);
            return ToResponse(result);
        }
        catch (Exception exception) when (ToStatusCode(exception) is { } code)
        {
            throw new RpcException(new Status(code, Describe(code)));
        }
    }

    // Un pedido mal formado no llega a Business: Booking lo recibe como INVALID_ARGUMENT y no reintenta.
    private Guid ValidateRequest(ChargeRequest request)
    {
        // Solo el formato "D" (con guiones, sin llaves): es el que manda Booking con Guid.ToString(), y
        // aceptar varios formatos para la misma clave de idempotencia solo agrega ambigüedad.
        // El largo se chequea aparte: TryParseExact ignora espacios al inicio y al final, y " <guid> "
        // llegaría como un id distinto del que Booking mandó.
        if (request.ReservationId.Length != GuidFormatDLength ||
            !Guid.TryParseExact(request.ReservationId, "D", out var reservationId) ||
            reservationId == Guid.Empty)
            throw InvalidArgument("reservation_id debe ser un GUID con formato xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.");

        var maxAmountCents = options.Value.MaxAmountCents;
        if (request.AmountCents <= 0 || request.AmountCents > maxAmountCents)
            throw InvalidArgument($"amount_cents debe estar entre 1 y {maxAmountCents}.");

        // Ordinal: "ars" no es "ARS". La lista sale de Charge:SupportedCurrencies; ampliarla no cambia el contrato.
        var supportedCurrencies = options.Value.SupportedCurrencies;
        if (!supportedCurrencies.Contains(request.Currency, StringComparer.Ordinal))
            throw InvalidArgument($"currency no soportada. Monedas aceptadas: {string.Join(", ", supportedCurrencies)}.");

        return reservationId;
    }

    // El header viene de afuera y termina en cada log del cobro: un valor sin límite llenaría los logs
    // (se probó con 20.000 caracteres). Kestrel ya rechaza los saltos de línea.
    // Un valor inválido se reemplaza y no se corta: uno cortado parece válido pero ya no coincide con el
    // de Booking, y en el cotejo confunde más que un marcador explícito. Nunca se rechaza el cobro por esto.
    private static string? SanitizeCorrelationId(string? value)
    {
        // Sin header: Booking puede no mandarlo, y el log muestra CorrelationId:(null).
        if (string.IsNullOrEmpty(value))
            return null;

        var isValid = value.Length <= MaxCorrelationIdLength &&
            value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

        return isValid ? value : InvalidCorrelationId;
    }

    private static RpcException InvalidArgument(string detail) =>
        new(new Status(StatusCode.InvalidArgument, detail));

    private static ChargeResponse ToResponse(ChargeResult result) => new()
    {
        Status = result.Outcome == ChargeOutcome.Approved
            ? ChargeStatus.Approved
            : ChargeStatus.Declined,
        ChargeId = result.ChargeId.ToString(),
        // Los string de Protobuf no aceptan null.
        FailureReason = result.DeclineReason ?? string.Empty
    };

    // Devuelve null para dejar pasar la excepción sin traducir: gRPC responde UNKNOWN y Booking la
    // manda a la DLQ para revisión manual.
    private static StatusCode? ToStatusCode(Exception exception) => exception switch
    {
        // Falla técnica de la pasarela: el cobro quedó Failed, sin cargo, y Booking puede reintentar.
        HttpRequestException => StatusCode.Unavailable,
        // Base caída (conexión rechazada, timeout): Booking reintenta y, si se agota, registra el posible
        // cobro tardío, que se coteja por reservation_id. Solo las transitorias: una contraseña mal
        // configurada o un error de SQL no se arreglan reintentando. DbException y no la de EF, para no
        // atar la presentación al ORM. EF la envuelve al guardar (InvalidOperationException >
        // DbUpdateException > NpgsqlException), por eso se recorre toda la cadena y este caso va antes
        // que InvalidOperationException.
        _ when HasTransientDatabaseFailure(exception) => StatusCode.Unavailable,
        // Cobro en curso (Pending): ABORTED y no UNAVAILABLE, porque Payment sí está accesible.
        ChargeInProgressException => StatusCode.Aborted,
        // Inconsistencia o bug (incluye ObjectDisposedException, que hereda de esta): reintentar
        // no lo arregla, así que va a revisión manual en vez de gastar los reintentos de Booking.
        InvalidOperationException => StatusCode.Internal,
        // Mismo reservation_id con otro monto o moneda: reintentar no lo arregla, requiere revisión.
        ChargeMismatchException => StatusCode.InvalidArgument,
        // Modo simulado mal configurado: reintentar no lo arregla.
        NotSupportedException => StatusCode.Internal,
        _ => null
    };

    // Recorre la cadena completa de InnerException: la profundidad del envoltorio depende de EF y de
    // la operación (guardar, consultar), así que no se asume un nivel fijo.
    private static bool HasTransientDatabaseFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { IsTransient: true })
                return true;
        }

        return false;
    }

    // Textos genéricos: el repositorio es público y un error interno no debe filtrar detalles.
    private static string Describe(StatusCode code) => code switch
    {
        StatusCode.Aborted => "Ya hay un cobro en curso para esta reserva.",
        StatusCode.Unavailable => "Payment no puede procesar el cobro en este momento. Reintentar.",
        StatusCode.InvalidArgument => "El pedido no coincide con el cobro registrado para esta reserva.",
        _ => "Error interno de Payment."
    };
}
