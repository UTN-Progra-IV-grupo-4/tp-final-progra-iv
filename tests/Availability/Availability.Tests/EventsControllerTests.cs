using Availability.Application.Events;
using Availability.Infrastructure.Events;
using Availability.Web.Controllers;
using Availability.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Availability.Tests;

public class EventsControllerTests
{
    [Fact]
    public async Task AvailableSeat_CanBeSelected()
    {
        var controller = CreateController(new DemoSeatOccupancyQuery());
        var seatId = DemoCatalogData.SeatId(1, 1);
        var result = Assert.IsType<ViewResult>(await controller.Seats(DemoCatalogData.EventId(1), seatId.ToString(), default));
        var model = Assert.IsType<EventSeatsViewModel>(result.Model);
        Assert.Equal(seatId, model.SelectedSeatId);
        Assert.True(controller.ModelState.IsValid);
    }

    [Theory]
    [InlineData(1, 2)] // Ocupado en la demo.
    [InlineData(2, 1)] // Pertenece a otro evento.
    public async Task OccupiedOrForeignSeat_IsRejected(int eventNumber, int position)
    {
        var controller = CreateController(new DemoSeatOccupancyQuery());
        var result = Assert.IsType<ViewResult>(await controller.Seats(DemoCatalogData.EventId(1),
            DemoCatalogData.SeatId(eventNumber, position).ToString(), default));
        Assert.Null(Assert.IsType<EventSeatsViewModel>(result.Model).SelectedSeatId);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task UnknownOccupancy_DoesNotAllowSelection()
    {
        var controller = CreateController(new UnconfirmedSeatOccupancyStub());
        var result = Assert.IsType<ViewResult>(await controller.Seats(DemoCatalogData.EventId(1),
            DemoCatalogData.SeatId(1, 1).ToString(), default));
        Assert.Null(Assert.IsType<EventSeatsViewModel>(result.Model).SelectedSeatId);
        Assert.False(controller.ModelState.IsValid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("<script>")]
    public async Task InvalidSeatId_IsRejectedWithSpanishMessage(string selectedSeatId)
    {
        var controller = CreateController(new DemoSeatOccupancyQuery());
        var result = Assert.IsType<ViewResult>(await controller.Seats(DemoCatalogData.EventId(1), selectedSeatId, default));
        Assert.Null(Assert.IsType<EventSeatsViewModel>(result.Model).SelectedSeatId);
        Assert.False(controller.ModelState.IsValid);
        var message = controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage;
        Assert.StartsWith("El asiento elegido no está disponible", message);
    }

    [Fact]
    public async Task UnknownEvent_Returns404()
    {
        var controller = CreateController(new DemoSeatOccupancyQuery());
        Assert.IsType<NotFoundResult>(await controller.Seats(Guid.NewGuid(), null, default));
    }

    private static EventsController CreateController(ISeatOccupancyQuery occupancy)
        => new(new EventQueryService(new InMemoryEventCatalog(), occupancy), new EventDisplayOptions(true));
}
