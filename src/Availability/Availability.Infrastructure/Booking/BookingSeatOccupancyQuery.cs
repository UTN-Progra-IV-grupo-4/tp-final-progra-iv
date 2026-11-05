using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Availability.Application.Events;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure.Booking;

// Implementa únicamente el contrato HTTP del #21. Booking decide cuáles reservas ocupan.
public class BookingSeatOccupancyQuery(
    HttpClient httpClient,
    IBookingServiceTokenProvider tokenProvider,
    ILogger<BookingSeatOccupancyQuery> logger) : ISeatOccupancyQuery
{
    // El token pendiente se repite en cada visita pública: Warning solo la primera vez por proceso.
    private static int pendingTokenWarningLogged;

    public async Task<SeatOccupancy> GetOccupiedSeatsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (eventId == Guid.Empty || httpClient.BaseAddress is null)
        {
            logger.LogWarning("No se puede consultar ocupación: falta Booking:BaseUrl o el evento no es válido.");
            return Unknown(eventId);
        }

        // El límite incluye la obtención del token y la lectura de la respuesta.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(httpClient.Timeout);
        try
        {
            var token = await tokenProvider.GetAccessTokenAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
            {
                const string message = "Ocupación sin confirmar: no hay token de availability-service. Integración pendiente del #11.";
                if (Interlocked.Exchange(ref pendingTokenWarningLogged, 1) == 0)
                {
                    logger.LogWarning(message);
                }
                else
                {
                    logger.LogDebug(message);
                }
                return Unknown(eventId);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/events/{eventId:D}/occupied-seats");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                // 401/403, 404 y 5xx no equivalen a un evento con todos sus asientos libres.
                logger.LogWarning("Booking no confirmó la ocupación del evento {EventId}: HTTP {StatusCode}.",
                    eventId, (int)response.StatusCode);
                return Unknown(eventId);
            }

            if (!string.Equals(response.Content.Headers.ContentType?.MediaType,
                "application/json", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Booking devolvió un tipo de contenido inválido para ocupación.");
                return Unknown(eventId);
            }

            var data = await response.Content.ReadFromJsonAsync<OccupiedSeatsResponse>(timeout.Token);
            if (data is null || data.EventId != eventId || data.SeatIds is null ||
                data.SeatIds.Any(id => id == Guid.Empty) || data.SeatIds.Distinct().Count() != data.SeatIds.Length)
            {
                logger.LogWarning("Booking devolvió una respuesta de ocupación incompleta o inconsistente para {EventId}.", eventId);
                return Unknown(eventId);
            }

            // Solo un 200 válido confirma la lista, incluso si está vacía.
            return new SeatOccupancy(eventId, true, data.SeatIds.ToHashSet());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Se agotó el tiempo para consultar la ocupación de Booking.");
            return Unknown(eventId);
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("No se pudo contactar a Booking para consultar ocupación.");
            return Unknown(eventId);
        }
        catch (JsonException)
        {
            logger.LogWarning("Booking devolvió JSON inválido para la consulta de ocupación.");
            return Unknown(eventId);
        }
    }

    private static SeatOccupancy Unknown(Guid eventId) => new(eventId, false, new HashSet<Guid>());

    // DTO privado de transporte: Availability no referencia las capas internas de Booking.
    private sealed class OccupiedSeatsResponse
    {
        public Guid? EventId { get; init; }
        public Guid[]? SeatIds { get; init; }
    }
}
