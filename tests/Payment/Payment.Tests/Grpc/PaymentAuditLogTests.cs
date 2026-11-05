using Contracts.Payment;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Payment.Business;
using Payment.Data.Repositories;
using Payment.Grpc.Services;
using Payment.Tests.Data;
using Payment.Tests.TestDoubles;

namespace Payment.Tests.Grpc;

// Audit trail of the charge chain (ticket #24): start and end events with the correlation id received from
// Booking, written by different classes but tied together by the logging scope.
[Collection(PostgreSqlCollection.Name)]
public sealed class PaymentAuditLogTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private const long AmountCents = 150_000;
    private const string Currency = "ARS";
    private const string CorrelationId = "3f2b8d1e-0000-4000-8000-000000000024";

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePaymentGateway gateway = new();
    private readonly RecordingLogs logs = new();
    private readonly List<IAsyncDisposable> dbContexts = [];

    public Task InitializeAsync() => fixture.ResetAsync();

    public async Task DisposeAsync()
    {
        logs.Dispose();
        foreach (var dbContext in dbContexts)
            await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task Charge_LogsStartAndEndEventsWithServiceAndCorrelationId()
    {
        var reservationId = Guid.NewGuid();

        await CreateService().Charge(CreateRequest(reservationId), TestServerCallContext.Create(CorrelationId));

        var started = Assert.Single(logs.Entries, entry => entry.EventId == PaymentAuditEvents.ChargeStarted);
        var finished = Assert.Single(logs.Entries, entry => entry.EventId == PaymentAuditEvents.ChargeFinished);
        foreach (var entry in new[] { started, finished })
        {
            Assert.Contains(reservationId.ToString(), entry.Message);
            Assert.Contains(entry.Scopes, scope =>
                scope.Contains("Service:Payment", StringComparison.Ordinal) &&
                scope.Contains($"CorrelationId:{CorrelationId}", StringComparison.Ordinal));
        }

        Assert.Contains("Approved", finished.Message);
    }

    [Fact]
    public async Task Charge_WhenRetried_LogsReplayAsEndOfChainWithStoredState()
    {
        var reservationId = Guid.NewGuid();
        var service = CreateService();
        await service.Charge(CreateRequest(reservationId), TestServerCallContext.Create(CorrelationId));

        await service.Charge(CreateRequest(reservationId), TestServerCallContext.Create(CorrelationId));

        var replayed = Assert.Single(logs.Entries, entry => entry.EventId == PaymentAuditEvents.ChargeReplayed);
        Assert.Contains("Approved", replayed.Message);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task Charge_WithInvalidRequest_LogsRejectionWithErrorCodeAndCorrelationId()
    {
        var request = CreateRequest(Guid.NewGuid(), currency: "USD");

        await Assert.ThrowsAsync<RpcException>(() =>
            CreateService().Charge(request, TestServerCallContext.Create(CorrelationId)));

        var rejected = Assert.Single(logs.Entries, entry => entry.EventId == PaymentAuditEvents.ChargeRejected);
        Assert.Equal(LogLevel.Warning, rejected.Level);
        Assert.Contains("ErrorCode=InvalidArgument", rejected.Message);
        Assert.Contains(rejected.Scopes, scope => scope.Contains(CorrelationId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Charge_WhenGatewayFails_LogsRejectionAsUnavailable()
    {
        gateway.FailNextCall(new HttpRequestException("gateway down"));

        await Assert.ThrowsAsync<RpcException>(() =>
            CreateService().Charge(CreateRequest(Guid.NewGuid()), TestServerCallContext.Create(CorrelationId)));

        var rejected = Assert.Single(logs.Entries, entry => entry.EventId == PaymentAuditEvents.ChargeRejected);
        Assert.Contains("ErrorCode=Unavailable", rejected.Message);
    }

    [Theory]
    [InlineData("line\nbreak")]
    [InlineData("forged\r\nwarn: Payment fake entry")]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")]
    public async Task Charge_WithUnsafeCorrelationId_NeverWritesItToTheLogs(string correlationId)
    {
        await CreateService().Charge(CreateRequest(Guid.NewGuid()), TestServerCallContext.Create(correlationId));

        Assert.NotEmpty(logs.Entries);
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain(correlationId, entry.Message, StringComparison.Ordinal);
            Assert.All(entry.Scopes, scope => Assert.DoesNotContain(correlationId, scope, StringComparison.Ordinal));
        });
        Assert.Contains(logs.Entries, entry =>
            entry.Scopes.Any(scope => scope.Contains("CorrelationId:invalido", StringComparison.Ordinal)));
    }

    private PaymentGrpcService CreateService()
    {
        var chargeOptions = Options.Create(new ChargeOptions { SupportedCurrencies = [Currency] });
        var dbContext = fixture.CreateDbContext();
        dbContexts.Add(dbContext);
        var chargeService = new ChargeService(
            new ChargeRepository(dbContext), gateway, chargeOptions, time, logs.CreateLogger<ChargeService>());

        return new PaymentGrpcService(chargeService, chargeOptions, logs.CreateLogger<PaymentGrpcService>());
    }

    private static ChargeRequest CreateRequest(Guid reservationId, string currency = Currency) => new()
    {
        ReservationId = reservationId.ToString("D"),
        AmountCents = AmountCents,
        Currency = currency
    };
}
