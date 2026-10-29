using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Booking.Data;

public class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql("Host=localhost;Database=booking_design_time")
            .Options;

        return new BookingDbContext(options);
    }
}
