extern alias paymentgrpc;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Booking.Api.Messaging;
using Booking.Api.Payments;
using Booking.Business;
using Booking.Business.Payments;
using Booking.Business.Reservations;
using Booking.Data;
using Booking.Data.Models;
using Booking.Tests.Data;
using Contracts.Payment;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.Business;
using Payment.Business.Gateways;
using Payment.Data;
using Testcontainers.PostgreSql;
using PaymentGrpcService = paymentgrpc::Payment.Grpc.Services.PaymentGrpcService;
using StoredChargeStatus = Payment.Data.Entities.ChargeStatus;

namespace Booking.Tests.Payments;

// Exercises Booking's message handler, the generated gRPC client, Payment's real service and
// both PostgreSQL schemas. The delivery is in memory; no Azure Service Bus receiver is started.
[Collection(PostgreSqlCollection.Name)]
public sealed class BookingPaymentRealIntegrationTests(PostgreSqlFixture bookingDatabase) : IAsyncLifetime
{
    private readonly PostgreSqlContainer paymentDatabase = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("payment_booking_tests")
        .WithUsername("payment_booking_tests")
        .WithPassword("payment_booking_tests")
        .Build();

    public async Task InitializeAsync()
    {
        await paymentDatabase.StartAsync();
        await using var context = CreatePaymentContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => paymentDatabase.DisposeAsync().AsTask();

    [Theory]
    [InlineData(SimulatedMode.Approve, ReservationStatus.Confirmed, StoredChargeStatus.Approved)]
    [InlineData(SimulatedMode.Decline, ReservationStatus.Rejected, StoredChargeStatus.Declined)]
    public async Task ApprovedOrDeclined_PersistsMatchingStatesInBothDatabasesAndSafeCorrelation(
        SimulatedMode mode,
        ReservationStatus reservationStatus,
        StoredChargeStatus chargeStatus)
    {
        await ResetAsync();
        await using var harness = await Harness.StartAsync(
            bookingDatabase, paymentDatabase.GetConnectionString(), mode);

        var settlement = await harness.ProcessAsync();
        var reservation = await ReadReservationAsync(harness.RequestId);
        var charge = await ReadChargeAsync(harness.RequestId);

        Assert.Equal(1, settlement.Completed);
        Assert.Empty(settlement.DeadLetters);
        Assert.Equal(reservationStatus, reservation.Status);
        Assert.Equal(mode == SimulatedMode.Decline
            ? ReservationRejectionReason.PaymentFailed : null, reservation.RejectionReason);
        Assert.Equal(chargeStatus, charge.Status);
        Assert.Equal(150000, charge.AmountCents);
        Assert.Equal("ARS", charge.Currency);
        Assert.Contains("correlation-19", harness.PaymentLogs.CorrelationIds);
        Assert.Contains(harness.BookingLogs.Messages, message =>
            message.Contains("correlation-19", StringComparison.Ordinal) &&
            message.Contains(harness.RequestId.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(harness.BookingLogs.Messages, message =>
            message.Contains("secret-user-payload", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.PaymentLogs.Messages, message =>
            message.Contains("secret-user-payload", StringComparison.Ordinal));
        Assert.Contains(harness.ChargeServiceLogs.Messages, message =>
            message.Contains(harness.RequestId.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(harness.ChargeServiceLogs.Messages, message =>
            message.Contains("secret-user-payload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ErrorMode_RetriesSameReservationAndPersistsPaymentUnavailable()
    {
        await ResetAsync();
        await using var harness = await Harness.StartAsync(
            bookingDatabase, paymentDatabase.GetConnectionString(), SimulatedMode.Error);

        var settlement = await harness.ProcessAsync();
        var reservation = await ReadReservationAsync(harness.RequestId);
        var charge = await ReadChargeAsync(harness.RequestId);

        Assert.Equal(1, settlement.Completed);
        Assert.Empty(settlement.DeadLetters);
        Assert.Equal(ReservationStatus.Rejected, reservation.Status);
        Assert.Equal(ReservationRejectionReason.PaymentUnavailable, reservation.RejectionReason);
        Assert.Equal(StoredChargeStatus.Failed, charge.Status);
        Assert.Equal(3, harness.PaymentLogs.CorrelationIds.Count);
        Assert.Contains(harness.BookingLogs.Messages, message =>
            message.Contains("PaymentOutcomeUnknown", StringComparison.Ordinal) &&
            message.Contains(harness.RequestId.ToString(), StringComparison.Ordinal));
        Assert.Contains("correlation-19", harness.PaymentLogs.CorrelationIds);
    }

    [Fact]
    public async Task SlowMode_DeadlineThenPending_DeadLettersAndChargeFinishesOnce()
    {
        await ResetAsync();
        await using var harness = await Harness.StartAsync(
            bookingDatabase, paymentDatabase.GetConnectionString(), SimulatedMode.Slow,
            TimeSpan.FromMilliseconds(500));

        var settlement = await harness.ProcessAsync();
        var reservation = await ReadReservationAsync(harness.RequestId);
        var charge = await ReadChargeAsync(harness.RequestId);

        Assert.Equal(0, settlement.Completed);
        Assert.Equal(ReservationRequestSettlementErrorCodes.PaymentPending,
            Assert.Single(settlement.DeadLetters).Reason);
        Assert.Equal(ReservationStatus.Retained, reservation.Status);
        Assert.Equal(StoredChargeStatus.Pending, charge.Status);
        Assert.Contains(harness.BookingLogs.Messages, message =>
            message.Contains("PaymentOutcomeUnknown", StringComparison.Ordinal) &&
            message.Contains("DeadLetter", StringComparison.Ordinal));

        var until = DateTime.UtcNow.AddSeconds(4);
        while (charge.Status == StoredChargeStatus.Pending && DateTime.UtcNow < until)
        {
            await Task.Delay(100);
            charge = await ReadChargeAsync(harness.RequestId);
        }

        Assert.Equal(StoredChargeStatus.Approved, charge.Status);
        Assert.Equal(ReservationStatus.Retained, (await ReadReservationAsync(harness.RequestId)).Status);
        await using var context = CreatePaymentContext();
        Assert.Equal(1, await context.Charges.CountAsync(item => item.ReservationId == harness.RequestId));

        var directRequestId = Guid.NewGuid();
        var timeout = await Assert.ThrowsAsync<PaymentChargeException>(() =>
            harness.Adapter.ChargeAsync(new PaymentChargeRequest(
                directRequestId, 150000, "ARS", "correlation-19")));
        Assert.Equal(PaymentChargeFailure.DeadlineExceeded, timeout.Failure);
        var directCharge = await ReadChargeAsync(directRequestId);
        until = DateTime.UtcNow.AddSeconds(4);
        while (directCharge.Status == StoredChargeStatus.Pending && DateTime.UtcNow < until)
        {
            await Task.Delay(100);
            directCharge = await ReadChargeAsync(directRequestId);
        }
        Assert.Equal(StoredChargeStatus.Approved, directCharge.Status);
    }

    [Fact]
    public async Task RepeatedReservationId_ReturnsSameChargeWithoutCreatingAnotherRow()
    {
        await ResetAsync();
        await using var harness = await Harness.StartAsync(
            bookingDatabase, paymentDatabase.GetConnectionString(), SimulatedMode.Approve);

        var firstSettlement = await harness.ProcessAsync();
        var firstCharge = await ReadChargeAsync(harness.RequestId);
        var repeated = await harness.Adapter.ChargeAsync(new PaymentChargeRequest(
            harness.RequestId, 150000, "ARS", "correlation-19"));
        var redeliverySettlement = await harness.ProcessAsync();

        Assert.Equal(1, firstSettlement.Completed);
        Assert.Equal(1, redeliverySettlement.Completed);
        Assert.Equal(PaymentChargeOutcome.Approved, repeated.Outcome);
        Assert.Equal(firstCharge.Id.ToString(), repeated.ChargeId);
        await using var context = CreatePaymentContext();
        Assert.Equal(1, await context.Charges.CountAsync(item => item.ReservationId == harness.RequestId));
        Assert.Equal(ReservationStatus.Confirmed, (await ReadReservationAsync(harness.RequestId)).Status);
    }

    [Fact]
    public async Task SeparatePaymentProcess_ReceivesBookingChargeOverLocalHttp2()
    {
        await ResetAsync();
        await using var paymentProcess = await PaymentProcess.StartAsync(
            paymentDatabase.GetConnectionString());
        await using var harness = await Harness.StartAsync(
            bookingDatabase,
            paymentDatabase.GetConnectionString(),
            SimulatedMode.Approve,
            externalPaymentAddress: paymentProcess.Address);

        var settlement = await harness.ProcessAsync();
        var reservation = await ReadReservationAsync(harness.RequestId);
        var charge = await ReadChargeAsync(harness.RequestId);
        await paymentProcess.WaitForLogAsync(line =>
            line.Contains(harness.RequestId.ToString(), StringComparison.Ordinal));
        await paymentProcess.WaitForLogAsync(line =>
            line.Contains("correlation-19", StringComparison.Ordinal));

        Assert.Equal(1, settlement.Completed);
        Assert.Empty(settlement.DeadLetters);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(StoredChargeStatus.Approved, charge.Status);
        Assert.Equal(150000, charge.AmountCents);
        Assert.Equal("ARS", charge.Currency);
        Assert.Contains(harness.BookingLogs.Messages, message =>
            message.Contains("correlation-19", StringComparison.Ordinal) &&
            message.Contains(harness.RequestId.ToString(), StringComparison.Ordinal));
        Assert.Contains(paymentProcess.LogLines, line =>
            line.Contains(harness.RequestId.ToString(), StringComparison.Ordinal));
        Assert.Contains(paymentProcess.LogLines, line =>
            line.Contains("correlation-19", StringComparison.Ordinal));
        Assert.DoesNotContain(paymentProcess.LogLines, line =>
            line.Contains("secret-user-payload", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.BookingLogs.Messages, message =>
            message.Contains("secret-user-payload", StringComparison.Ordinal));
    }

    private PaymentDbContext CreatePaymentContext() => new(
        new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(paymentDatabase.GetConnectionString()).Options);

    private async Task ResetAsync()
    {
        await bookingDatabase.ResetAsync();
        await using var context = CreatePaymentContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE charges;");
    }

    private async Task<Reservation> ReadReservationAsync(Guid id)
    {
        await using var context = bookingDatabase.CreateDbContext();
        return await context.Reservations.AsNoTracking().SingleAsync(item => item.Id == id);
    }

    private async Task<Payment.Data.Entities.Charge> ReadChargeAsync(Guid reservationId)
    {
        await using var context = CreatePaymentContext();
        return await context.Charges.AsNoTracking().SingleAsync(item => item.ReservationId == reservationId);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplication? paymentApp;
        private readonly GrpcChannel channel;
        private readonly ServiceProvider bookingServices;
        private readonly ReservationRequestMessageProcessor processor;

        public Guid RequestId { get; } = Guid.NewGuid();
        public Guid EventId { get; } = Guid.NewGuid();
        public Guid SeatId { get; } = Guid.NewGuid();
        public GrpcPaymentChargeClient Adapter { get; }
        public CapturingLogger<PaymentGrpcService> PaymentLogs { get; } = new();
        public CapturingLogger<ChargeService> ChargeServiceLogs { get; } = new();
        public CapturingLogger<ReservationRequestMessageProcessor> BookingLogs { get; } = new();

        private Harness(
            PostgreSqlFixture bookingDatabase,
            string paymentConnectionString,
            SimulatedMode mode,
            TimeSpan deadline,
            string? externalPaymentAddress)
        {
            if (externalPaymentAddress is null)
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseTestServer();
                builder.Services.AddPaymentBusiness(paymentConnectionString);
                builder.Services.Configure<SimulatedGatewayOptions>(options =>
                {
                    options.Mode = mode;
                    options.SlowDelaySeconds = 1;
                });
                builder.Services.Configure<ChargeOptions>(options =>
                {
                    options.SupportedCurrencies = ["ARS"];
                    options.PendingTimeoutSeconds = 60;
                });
                builder.Services.AddSingleton<ILogger<PaymentGrpcService>>(PaymentLogs);
                builder.Services.AddSingleton<ILogger<ChargeService>>(ChargeServiceLogs);
                builder.Services.AddGrpc();
                paymentApp = builder.Build();
                paymentApp.MapGrpcService<PaymentGrpcService>();
                paymentApp.StartAsync().GetAwaiter().GetResult();

                channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
                {
                    HttpHandler = paymentApp.GetTestServer().CreateHandler()
                });
            }
            else
            {
                channel = GrpcChannel.ForAddress(externalPaymentAddress);
            }
            var paymentOptions = Options.Create(new PaymentProcessingOptions
            {
                Enabled = true,
                AmountCents = 150000,
                Currency = "ARS",
                MaxAttempts = 3,
                RetryDelay = TimeSpan.FromMilliseconds(10),
                Deadline = deadline
            });
            Adapter = new GrpcPaymentChargeClient(
                new PaymentService.PaymentServiceClient(channel), paymentOptions);

            var services = new ServiceCollection();
            using var bookingContext = bookingDatabase.CreateDbContext();
            services.AddBookingBusiness(bookingContext.Database.GetConnectionString()!);
            services.AddSingleton(Options.Create(new ReservationProcessingOptions
            {
                RetentionDuration = TimeSpan.FromMinutes(10)
            }));
            services.AddSingleton<IOptions<PaymentProcessingOptions>>(paymentOptions);
            services.AddScoped<IPaymentChargeClient>(_ => Adapter);
            bookingServices = services.BuildServiceProvider();
            var handler = new ReservationRequestMessageHandler(
                bookingServices.GetRequiredService<IServiceScopeFactory>(),
                new ReservationRequestMapper(),
                Options.Create(new PaymentConsumerOptions { Enabled = true }));
            processor = new ReservationRequestMessageProcessor(handler, BookingLogs);
        }

        public static Task<Harness> StartAsync(
            PostgreSqlFixture bookingDatabase,
            string paymentConnectionString,
            SimulatedMode mode,
            TimeSpan? deadline = null,
            string? externalPaymentAddress = null) => Task.FromResult(new Harness(
                bookingDatabase, paymentConnectionString, mode,
                deadline ?? TimeSpan.FromSeconds(5), externalPaymentAddress));

        public async Task<RecordingSettlement> ProcessAsync()
        {
            var settlement = new RecordingSettlement();
            var delivery = new ReservationRequestDelivery(
                BinaryData.FromString($$"""
                  {
                    "messageId": "{{RequestId}}",
                    "correlationId": "correlation-19",
                    "eventId": "{{EventId}}",
                    "seatId": "{{SeatId}}",
                    "userId": "secret-user-payload",
                    "requestedAt": "{{DateTimeOffset.UtcNow:O}}"
                  }
                  """),
                RequestId.ToString(), "transport-correlation", null);
            await processor.ProcessAsync(delivery, settlement, CancellationToken.None);
            return settlement;
        }

        public async ValueTask DisposeAsync()
        {
            await bookingServices.DisposeAsync();
            channel.Dispose();
            if (paymentApp is not null)
                await paymentApp.DisposeAsync();
        }
    }

    private sealed class PaymentProcess : IAsyncDisposable
    {
        private readonly Process process;
        public string Address { get; }
        public ConcurrentQueue<string> LogLines { get; } = new();

        private PaymentProcess(Process process, string address)
        {
            this.process = process;
            Address = address;
            process.OutputDataReceived += (_, eventArgs) => AddLog(eventArgs.Data);
            process.ErrorDataReceived += (_, eventArgs) => AddLog(eventArgs.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public static async Task<PaymentProcess> StartAsync(string connectionString)
        {
            var portPicker = new TcpListener(IPAddress.Loopback, 0);
            portPicker.Start();
            var port = ((IPEndPoint)portPicker.LocalEndpoint).Port;
            portPicker.Stop();
            var address = $"http://127.0.0.1:{port}";

            var assemblyPath = typeof(PaymentGrpcService).Assembly.Location;
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = FindPaymentProjectDirectory()
            };
            startInfo.ArgumentList.Add(assemblyPath);
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            startInfo.Environment["ConnectionStrings__Payment"] = connectionString;
            startInfo.Environment["SimulatedGateway__Mode"] = "Approve";
            startInfo.Environment["Charge__SupportedCurrencies__0"] = "ARS";
            startInfo.Environment["Charge__PendingTimeoutSeconds"] = "60";
            startInfo.Environment["Kestrel__Endpoints__Grpc__Url"] = address;
            startInfo.Environment["Kestrel__Endpoints__Grpc__Protocols"] = "Http2";

            var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Could not start local Payment process.");
            var instance = new PaymentProcess(process, address);
            try
            {
                var until = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < until)
                {
                    if (process.HasExited)
                        throw new InvalidOperationException("Local Payment process exited before listening.");

                    try
                    {
                        using var socket = new TcpClient();
                        await socket.ConnectAsync(IPAddress.Loopback, port);
                        return instance;
                    }
                    catch (SocketException)
                    {
                        await Task.Delay(100);
                    }
                }

                throw new TimeoutException("Local Payment process did not listen within 15 seconds.");
            }
            catch
            {
                await instance.DisposeAsync();
                throw;
            }
        }

        private void AddLog(string? line)
        {
            if (line is not null)
                LogLines.Enqueue(line);
        }

        public async Task WaitForLogAsync(Func<string, bool> predicate)
        {
            var until = DateTime.UtcNow.AddSeconds(3);
            while (!LogLines.Any(predicate) && DateTime.UtcNow < until)
                await Task.Delay(25);
        }

        private static string FindPaymentProjectDirectory()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                var projectDirectory = Path.Combine(directory.FullName, "src", "Payment", "Payment.Grpc");
                if (File.Exists(Path.Combine(projectDirectory, "Payment.Grpc.csproj")))
                    return projectDirectory;
            }

            throw new DirectoryNotFoundException("Payment.Grpc project directory was not found.");
        }

        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            process.Dispose();
        }
    }

    private sealed class RecordingSettlement : IReservationRequestMessageSettlement
    {
        public int Completed { get; private set; }
        public List<(string Reason, string Description)> DeadLetters { get; } = [];

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            Completed++;
            return Task.CompletedTask;
        }

        public Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken)
        {
            DeadLetters.Add((reason, description));
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string?> CorrelationIds { get; } = new();
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (var property in properties)
                {
                    if (property.Key == "CorrelationId")
                        CorrelationIds.Enqueue(property.Value as string);
                }
            }

            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception));
    }
}
