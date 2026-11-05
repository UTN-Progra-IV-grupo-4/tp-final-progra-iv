using Booking.Api.Payments;
using Booking.Business.Payments;
using Booking.Tests.TestDoubles;
using Contracts.Payment;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Payments;

public sealed class GrpcPaymentChargeClientTests
{
    [Theory]
    [InlineData(ChargeStatus.Approved, PaymentChargeOutcome.Approved)]
    [InlineData(ChargeStatus.Declined, PaymentChargeOutcome.Declined)]
    public async Task ChargeAsync_UsesSharedContractAndPropagatesMetadataAndDeadline(
        ChargeStatus status,
        PaymentChargeOutcome expected)
    {
        ChargeRequest? capturedRequest = null;
        string? capturedCorrelationId = null;
        DateTime? capturedDeadline = null;
        using var harness = new GrpcHarness((request, context) =>
        {
            capturedRequest = request;
            capturedCorrelationId = context.RequestHeaders.GetValue("x-correlation-id");
            capturedDeadline = context.Deadline;
            return Task.FromResult(new ChargeResponse
            {
                Status = status,
                ChargeId = "charge-123"
            });
        });
        var reservationId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var result = await harness.Adapter.ChargeAsync(new PaymentChargeRequest(
            reservationId, 150000, "ARS", "correlation-19"));
        var after = DateTime.UtcNow;

        Assert.Equal(expected, result.Outcome);
        Assert.Equal("charge-123", result.ChargeId);
        Assert.Equal(reservationId.ToString("D"), capturedRequest!.ReservationId);
        Assert.Equal(150000, capturedRequest.AmountCents);
        Assert.Equal("ARS", capturedRequest.Currency);
        Assert.Equal("correlation-19", capturedCorrelationId);
        Assert.InRange(capturedDeadline!.Value, before.AddSeconds(5), after.AddSeconds(5));
    }

    [Theory]
    [InlineData(StatusCode.Unavailable, PaymentChargeFailure.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded, PaymentChargeFailure.DeadlineExceeded)]
    [InlineData(StatusCode.Aborted, PaymentChargeFailure.Pending)]
    [InlineData(StatusCode.InvalidArgument, PaymentChargeFailure.InvalidRequest)]
    [InlineData(StatusCode.Internal, PaymentChargeFailure.UnexpectedResponse)]
    [InlineData(StatusCode.Unknown, PaymentChargeFailure.UnexpectedResponse)]
    [InlineData(StatusCode.Unauthenticated, PaymentChargeFailure.UnexpectedResponse)]
    [InlineData(StatusCode.PermissionDenied, PaymentChargeFailure.UnexpectedResponse)]
    public async Task ChargeAsync_MapsGrpcErrorsToBookingFailures(
        StatusCode status,
        PaymentChargeFailure expected)
    {
        using var harness = new GrpcHarness((_, _) =>
            throw new RpcException(new Status(status, "server detail must not be exposed")));

        var exception = await Assert.ThrowsAsync<PaymentChargeException>(() =>
            harness.Adapter.ChargeAsync(new PaymentChargeRequest(
                Guid.NewGuid(), 150000, "ARS", null)));

        Assert.Equal(expected, exception.Failure);
        Assert.DoesNotContain("server detail", exception.Message);
    }

    [Theory]
    [InlineData(ChargeStatus.Unspecified)]
    [InlineData(ChargeStatus.Approved)]
    public async Task ChargeAsync_RejectsUnspecifiedStatusOrMissingChargeId(
        ChargeStatus status)
    {
        using var harness = new GrpcHarness((_, _) => Task.FromResult(new ChargeResponse
        {
            Status = status,
            ChargeId = status == ChargeStatus.Unspecified ? "charge-123" : ""
        }));

        var exception = await Assert.ThrowsAsync<PaymentChargeException>(() =>
            harness.Adapter.ChargeAsync(new PaymentChargeRequest(
                Guid.NewGuid(), 150000, "ARS", null)));

        Assert.Equal(PaymentChargeFailure.UnexpectedResponse, exception.Failure);
    }

    [Fact]
    public async Task ChargeAsync_WhenDeadlineExpires_ReportsDeadlineExceeded()
    {
        using var harness = new GrpcHarness(async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            return new ChargeResponse { Status = ChargeStatus.Approved, ChargeId = "late" };
        }, TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<PaymentChargeException>(() =>
            harness.Adapter.ChargeAsync(new PaymentChargeRequest(
                Guid.NewGuid(), 150000, "ARS", null)));

        Assert.Equal(PaymentChargeFailure.DeadlineExceeded, exception.Failure);
    }

    [Fact]
    public async Task ChargeAsync_LogsRequestAndAnswerWithCorrelationIdAndWithoutTheAmount()
    {
        using var logs = new RecordingLogs();
        using var harness = new GrpcHarness(
            (_, _) => Task.FromResult(new ChargeResponse { Status = ChargeStatus.Declined, ChargeId = "charge-24" }),
            logger: logs.CreateLogger<GrpcPaymentChargeClient>());
        var reservationId = Guid.NewGuid();

        await harness.Adapter.ChargeAsync(new PaymentChargeRequest(reservationId, 150000, "ARS", "correlation-24"));

        var requested = Assert.Single(logs.Entries, entry => entry.EventId.Id == 2010);
        var answered = Assert.Single(logs.Entries, entry => entry.EventId.Id == 2011);
        foreach (var entry in new[] { requested, answered })
        {
            Assert.Contains("Service: Booking", entry.Message);
            Assert.Contains($"RequestId: {reservationId}", entry.Message);
            Assert.Contains("CorrelationId: correlation-24", entry.Message);
            Assert.DoesNotContain("150000", entry.Message);
        }

        Assert.Contains("ChargeId: charge-24", answered.Message);
        Assert.Contains("PaymentOutcome: Declined", answered.Message);
    }

    [Fact]
    public async Task ChargeAsync_WhenPaymentIsUnavailable_LogsTheFailureKindAsErrorCode()
    {
        using var logs = new RecordingLogs();
        using var harness = new GrpcHarness(
            (_, _) => throw new RpcException(new Status(StatusCode.Unavailable, "down")),
            logger: logs.CreateLogger<GrpcPaymentChargeClient>());

        await Assert.ThrowsAsync<PaymentChargeException>(() => harness.Adapter.ChargeAsync(
            new PaymentChargeRequest(Guid.NewGuid(), 150000, "ARS", "correlation-24")));

        var failed = Assert.Single(logs.Entries, entry => entry.EventId.Id == 2012);
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Contains("ErrorCode: Unavailable", failed.Message);
        Assert.Contains("CorrelationId: correlation-24", failed.Message);
    }

    private sealed class GrpcHarness : IDisposable
    {
        private readonly WebApplication app;
        private readonly TestServer server;
        private readonly GrpcChannel channel;

        public GrpcPaymentChargeClient Adapter { get; }

        public GrpcHarness(
            Func<ChargeRequest, ServerCallContext, Task<ChargeResponse>> handler,
            TimeSpan? deadline = null,
            ILogger<GrpcPaymentChargeClient>? logger = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddGrpc();
            builder.Services.AddSingleton(new TestPaymentService(handler));
            app = builder.Build();
            app.MapGrpcService<TestPaymentService>();
            app.StartAsync().GetAwaiter().GetResult();
            server = app.GetTestServer();
            channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpHandler = server.CreateHandler()
            });
            Adapter = new GrpcPaymentChargeClient(
                new PaymentService.PaymentServiceClient(channel),
                Options.Create(new PaymentProcessingOptions
                {
                    Deadline = deadline ?? TimeSpan.FromSeconds(5)
                }),
                logger ?? NullLogger<GrpcPaymentChargeClient>.Instance);
        }

        public void Dispose()
        {
            channel.Dispose();
            server.Dispose();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public sealed class TestPaymentService(
        Func<ChargeRequest, ServerCallContext, Task<ChargeResponse>> handler)
        : PaymentService.PaymentServiceBase
    {
        public override Task<ChargeResponse> Charge(
            ChargeRequest request,
            ServerCallContext context) => handler(request, context);
    }
}
