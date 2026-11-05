using Booking.Business.Services;
using Booking.Data.Models;
using Booking.Tests.TestDoubles;

namespace Booking.Tests.Business;

public class ReservationStatusServiceTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, "Expired")]
    [InlineData(0, "Expired")]
    [InlineData(1, "Retained")]
    [InlineData(null, "Retained")]
    public async Task GetStatusAsync_RetainedReservation_ReturnsEffectiveStatus(
        int? millisecondsUntilExpiry,
        string expectedStatus)
    {
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            Status = ReservationStatus.Retained,
            ExpiresAt = millisecondsUntilExpiry.HasValue
                ? UtcNow.AddMilliseconds(millisecondsUntilExpiry.Value)
                : null
        };

        var repository = new StubReservationRepository();
        repository.ReservationsById[reservation.Id] = reservation;
        var service = new ReservationStatusService(
            repository, new FixedTimeProvider(UtcNow));

        var response = await service.GetStatusAsync(reservation.Id);

        Assert.NotNull(response);
        Assert.Equal(reservation.Id, response.RequestId);
        Assert.Equal(expectedStatus, response.Status);
        Assert.Null(response.RejectionReason);

        // La consulta calcula el estado visible sin modificar la entidad.
        Assert.Equal(ReservationStatus.Retained, reservation.Status);
    }

    [Fact]
    public async Task GetStatusAsync_RejectedWithoutReason_ThrowsInvalidOperationException()
    {
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            Status = ReservationStatus.Rejected,
            RejectionReason = null
        };

        var repository = new StubReservationRepository();
        repository.ReservationsById[reservation.Id] = reservation;
        var service = new ReservationStatusService(
            repository, new FixedTimeProvider(UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetStatusAsync(reservation.Id));
    }
}
