using Booking.Api.Logging;
using Booking.Business.Payments;
using Contracts;
using Contracts.Payment;
using Grpc.Core;
using Microsoft.Extensions.Options;
using ServiceAuthentication;

namespace Booking.Api.Payments;

// The transport adapter keeps generated protobuf types and gRPC status codes out of Business.
internal sealed class GrpcPaymentChargeClient(
    PaymentService.PaymentServiceClient client,
    IOptions<PaymentProcessingOptions> options,
    ILogger<GrpcPaymentChargeClient> logger) : IPaymentChargeClient
{
    public async Task<PaymentChargeResult> ChargeAsync(
        PaymentChargeRequest request,
        CancellationToken cancellationToken = default)
    {
        var headers = new Metadata();
        if (!string.IsNullOrEmpty(request.CorrelationId))
        {
            headers.Add(CorrelationIdPolicy.HeaderName, request.CorrelationId);
        }

        var chargeRequest = new ChargeRequest
        {
            ReservationId = request.ReservationId.ToString("D"),
            AmountCents = request.AmountCents,
            Currency = request.Currency
        };

        // Only identifiers and the outcome are audited: neither the amount nor the token reach the logs.
        logger.LogInformation(
            BookingAuditEvents.PaymentChargeRequested,
            "Payment charge requested. Service: {Service}; RequestId: {RequestId}; CorrelationId: {CorrelationId}.",
            BookingAuditEvents.ServiceName,
            request.ReservationId,
            request.CorrelationId);

        var deadline = DateTime.UtcNow.Add(options.Value.Deadline);
        ChargeResponse response;
        try
        {
            response = await client.ChargeAsync(
                chargeRequest,
                headers,
                deadline: deadline,
                cancellationToken: cancellationToken);
        }
        catch (RpcException exception) when (
            exception.StatusCode == StatusCode.Cancelled && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (RpcException exception) when (exception.InnerException is ServiceTokenAcquisitionException)
        {
            // The service token could not be obtained, so no request reached Payment and no charge exists.
            LogChargeFailed(request, PaymentChargeFailure.Unavailable);
            throw new PaymentChargeException(PaymentChargeFailure.Unavailable, exception);
        }
        catch (RpcException exception) when (DateTime.UtcNow >= deadline)
        {
            LogChargeFailed(request, PaymentChargeFailure.DeadlineExceeded);
            throw new PaymentChargeException(PaymentChargeFailure.DeadlineExceeded, exception);
        }
        catch (RpcException exception)
        {
            var failure = MapFailure(exception.StatusCode);
            LogChargeFailed(request, failure);
            throw new PaymentChargeException(failure, exception);
        }

        if (string.IsNullOrWhiteSpace(response.ChargeId))
        {
            throw new PaymentChargeException(PaymentChargeFailure.UnexpectedResponse);
        }

        var result = response.Status switch
        {
            ChargeStatus.Approved => new PaymentChargeResult(
                PaymentChargeOutcome.Approved, response.ChargeId),
            ChargeStatus.Declined => new PaymentChargeResult(
                PaymentChargeOutcome.Declined, response.ChargeId),
            _ => throw new PaymentChargeException(PaymentChargeFailure.UnexpectedResponse)
        };

        logger.LogInformation(
            BookingAuditEvents.PaymentChargeAnswered,
            "Payment charge answered. Service: {Service}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; ChargeId: {ChargeId}; PaymentOutcome: {PaymentOutcome}.",
            BookingAuditEvents.ServiceName,
            request.ReservationId,
            request.CorrelationId,
            result.ChargeId,
            result.Outcome);
        return result;
    }

    private void LogChargeFailed(PaymentChargeRequest request, PaymentChargeFailure failure) =>
        logger.LogWarning(
            BookingAuditEvents.PaymentChargeFailed,
            "Payment charge attempt failed. Service: {Service}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; ErrorCode: {ErrorCode}.",
            BookingAuditEvents.ServiceName,
            request.ReservationId,
            request.CorrelationId,
            failure);

    private static PaymentChargeFailure MapFailure(StatusCode statusCode) => statusCode switch
    {
        StatusCode.Unavailable => PaymentChargeFailure.Unavailable,
        StatusCode.DeadlineExceeded => PaymentChargeFailure.DeadlineExceeded,
        StatusCode.Aborted => PaymentChargeFailure.Pending,
        StatusCode.InvalidArgument => PaymentChargeFailure.InvalidRequest,
        _ => PaymentChargeFailure.UnexpectedResponse
    };
}
