using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Booking.Data;

public class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    private const string ConnectionStringVariable = "ConnectionStrings__Booking";

    public BookingDbContext CreateDbContext(string[] args)
    {
        // The tools prefer this factory over the application host, so it must honor the same variable the API reads.
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? "Host=localhost;Database=booking_design_time";

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BookingDbContext(options);
    }
}
