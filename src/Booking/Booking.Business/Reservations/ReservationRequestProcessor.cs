using Booking.Data.Models;
using Booking.Data.Repositories;
using Microsoft.Extensions.Options;

namespace Booking.Business.Reservations;

public sealed class ReservationRequestProcessor : IReservationRequestProcessor
{
    private readonly IReservationRepository reservationRepository;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan retentionDuration;

    public ReservationRequestProcessor(
        IReservationRepository reservationRepository,
        IOptions<ReservationProcessingOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(reservationRepository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (options.Value.RetentionDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(ReservationProcessingOptions.RetentionDuration)} must be greater than zero.");
        }

        this.reservationRepository = reservationRepository;
        this.timeProvider = timeProvider;
        retentionDuration = options.Value.RetentionDuration;
    }

    public async Task<ReservationProcessingResult> ProcessAsync(
        ProcessReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationResult = Validate(request);
        if (validationResult is not null)
        {
            return validationResult;
        }

        // Idempotencia: el id de la solicitud es el id de la reserva. Si el broker entrega el mismo
        // mensaje dos veces, el segundo INSERT choca contra la clave primaria y se resuelve como
        // duplicado en vez de crear otra reserva. La exclusión por asiento la garantiza el índice
        // único parcial de la base, no una lectura previa que podría quedar obsoleta.
        var retainedReservation = new Reservation
        {
            Id = request.RequestId,
            EventId = request.EventId,
            SeatId = request.SeatId,
            Status = ReservationStatus.Retained,
            ExpiresAt = timeProvider.GetUtcNow().Add(retentionDuration)
        };

        var insertResult = await reservationRepository.TryInsertAsync(
            retainedReservation,
            cancellationToken);

        if (insertResult.Outcome == ReservationInsertOutcome.SeatTaken)
        {
            // El asiento puede estar bloqueado por una retención ya vencida que el proceso periódico
            // todavía no marcó como Expired. Se la vence acá, de forma condicional, antes de rechazar.
            var expiredReservation = await reservationRepository.TryExpireRetainedReservationForSeatAsync(
                request.EventId,
                request.SeatId,
                timeProvider.GetUtcNow(),
                cancellationToken);

            if (expiredReservation)
            {
                // Retry once: if the seat is still taken, another consumer won the race,
                // so return SeatTaken instead of continuing in a loop.
                insertResult = await reservationRepository.TryInsertAsync(
                    retainedReservation,
                    cancellationToken);
            }
        }

        // RequestIdExists: ya existe una reserva con este id, o sea que el mensaje se reentregó.
        // SeatTaken: un asiento activo de otra solicitud sigue ocupándolo, se persiste el rechazo.
        return insertResult.Outcome switch
        {
            ReservationInsertOutcome.Created => new ReservationProcessingResult(
                ReservationProcessingOutcome.Retained,
                request.RequestId),
            ReservationInsertOutcome.RequestIdExists => ResolveExistingRequest(
                request,
                RequireExistingReservation(insertResult)),
            ReservationInsertOutcome.SeatTaken => await PersistSeatTakenAsync(
                request,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unknown reservation insert outcome: {insertResult.Outcome}.")
        };
    }

    private async Task<ReservationProcessingResult> PersistSeatTakenAsync(
        ProcessReservationRequest request,
        CancellationToken cancellationToken)
    {
        var rejectedReservation = new Reservation
        {
            Id = request.RequestId,
            EventId = request.EventId,
            SeatId = request.SeatId,
            Status = ReservationStatus.Rejected,
            RejectionReason = ReservationRejectionReason.SeatTaken,
            ExpiresAt = null
        };

        var rejectedInsertResult = await reservationRepository.TryInsertAsync(
            rejectedReservation,
            cancellationToken);

        return rejectedInsertResult.Outcome switch
        {
            ReservationInsertOutcome.Created => new ReservationProcessingResult(
                ReservationProcessingOutcome.SeatTaken,
                request.RequestId),
            ReservationInsertOutcome.RequestIdExists => ResolveExistingRequest(
                request,
                RequireExistingReservation(rejectedInsertResult)),
            ReservationInsertOutcome.SeatTaken => throw new InvalidOperationException(
                "A rejected reservation unexpectedly conflicted with the active-seat index."),
            _ => throw new InvalidOperationException(
                $"Unknown reservation insert outcome: {rejectedInsertResult.Outcome}.")
        };
    }

    // Un id repetido con el mismo evento y asiento es una reentrega legítima (Duplicate). Con otro
    // evento o asiento es un id reutilizado para una solicitud distinta (RequestConflict): no se
    // reintenta, porque reprocesarlo nunca cambiaría el resultado.
    private static ReservationProcessingResult ResolveExistingRequest(
        ProcessReservationRequest request,
        Reservation existingReservation)
    {
        if (existingReservation.EventId != request.EventId ||
            existingReservation.SeatId != request.SeatId)
        {
            return new ReservationProcessingResult(
                ReservationProcessingOutcome.RequestConflict,
                request.RequestId,
                ReservationProcessingErrorCodes.RequestPayloadConflict);
        }

        return new ReservationProcessingResult(
            ReservationProcessingOutcome.Duplicate,
            request.RequestId);
    }

    private static Reservation RequireExistingReservation(ReservationInsertResult insertResult) =>
        insertResult.ExistingReservation ?? throw new InvalidOperationException(
            "RequestIdExists requires the existing reservation.");

    private static ReservationProcessingResult? Validate(ProcessReservationRequest request)
    {
        if (request.RequestId == Guid.Empty)
        {
            return Invalid(request.RequestId, ReservationProcessingErrorCodes.RequestIdRequired);
        }

        if (request.EventId == Guid.Empty)
        {
            return Invalid(request.RequestId, ReservationProcessingErrorCodes.EventIdRequired);
        }

        if (request.SeatId == Guid.Empty)
        {
            return Invalid(request.RequestId, ReservationProcessingErrorCodes.SeatIdRequired);
        }

        return null;
    }

    private static ReservationProcessingResult Invalid(Guid requestId, string errorCode) =>
        new(
            ReservationProcessingOutcome.InvalidRequest,
            requestId,
            ErrorCode: errorCode);
}
