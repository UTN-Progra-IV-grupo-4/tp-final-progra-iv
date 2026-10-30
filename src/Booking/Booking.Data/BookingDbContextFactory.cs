using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Booking.Data;

// Used only by the EF Core tools, so migrations can be created without a configured connection string.
public class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args)
    {
        // Same sources the API reads in Development: User Secrets, then environment variables.
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("TpFinal.Booking.Api")
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Booking")
            ?? "Host=localhost;Database=booking_design_time";

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BookingDbContext(options);
    }
}
