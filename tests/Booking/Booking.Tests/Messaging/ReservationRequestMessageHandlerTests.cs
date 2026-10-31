using Booking.Api.Messaging;
using Booking.Business.Reservations;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Tests.Messaging;

public sealed class ReservationRequestMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenMessageIsValid_DelegatesToScopedBusinessProcessor()
    {
        var processor = new RecordingReservationRequestProcessor();
        var services = new ServiceCollection();
        services.AddScoped<IReservationRequestProcessor>(_ => processor);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new ReservationRequestMessageHandler(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new ReservationRequestMapper());
        var requestId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var delivery = CreateDelivery(requestId, eventId, seatId);

        var result = await handler.HandleAsync(delivery, CancellationToken.None);

        Assert.True(result.WasMapped);
        Assert.Null(result.MappingErrorCode);
        Assert.NotNull(result.ProcessingResult);
        Assert.Equal(ReservationProcessingOutcome.Retained, result.ProcessingResult.Outcome);
        Assert.Equal(requestId, result.ProcessingResult.RequestId);
        var request = Assert.Single(processor.Requests);
        Assert.Equal(requestId, request.RequestId);
        Assert.Equal(eventId, request.EventId);
        Assert.Equal(seatId, request.SeatId);
    }

    [Fact]
    public async Task HandleAsync_WhenMessageIsInvalid_DoesNotResolveBusinessProcessor()
    {
        var services = new ServiceCollection();
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new ReservationRequestMessageHandler(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new ReservationRequestMapper());
        var delivery = new ReservationRequestDelivery(
            BinaryData.FromString("{invalid-json"),
            Guid.NewGuid().ToString(),
            null,
            null);

        var result = await handler.HandleAsync(delivery, CancellationToken.None);

        Assert.False(result.WasMapped);
        Assert.Equal(
            ReservationRequestMappingErrorCodes.InvalidJson,
            result.MappingErrorCode);
        Assert.Null(result.ProcessingResult);
    }

    private static ReservationRequestDelivery CreateDelivery(
        Guid requestId,
        Guid eventId,
        Guid seatId) =>
        new(
            BinaryData.FromString(
                $$"""
                  {
                    "requestId": "{{requestId}}",
                    "eventId": "{{eventId}}",
                    "seatId": "{{seatId}}"
                  }
                  """),
            requestId.ToString(),
            "test-correlation-id",
            null);

    private sealed class RecordingReservationRequestProcessor : IReservationRequestProcessor
    {
        public List<ProcessReservationRequest> Requests { get; } = [];

        public ReservationProcessingResult Result { get; } =
            new(ReservationProcessingOutcome.Retained, Guid.NewGuid());

        public Task<ReservationProcessingResult> ProcessAsync(
            ProcessReservationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result with { RequestId = request.RequestId });
        }
    }
}
