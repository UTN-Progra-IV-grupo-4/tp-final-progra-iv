using Booking.Business.Payments;
using Contracts.Payment;
using Grpc.Core;
using Microsoft.Extensions.Options;
using ServiceAuthentication;

namespace Booking.Api.Payments;

// The transport adapter keeps generated protobuf types and gRPC status codes out of Business.
internal sealed class GrpcPaymentChargeClient(
    PaymentService.PaymentServiceClient client,
    IOptions<PaymentProcessingOptions> options) : IPaymentChargeClient
{
    private const string CorrelationIdHeader = "x-correlation-id";

    public async Task<PaymentChargeResult> ChargeAsync(
        PaymentChargeRequest request,
        CancellationToken cancellationToken = default)
    {
        var headers = new Metadata();
        if (!string.IsNullOrEmpty(request.CorrelationId))
        {
            headers.Add(CorrelationIdHeader, request.CorrelationId);
        }

        var chargeRequest = new ChargeRequest
        {
            ReservationId = request.ReservationId.ToString("D"),
            AmountCents = request.AmountCents,
            Currency = request.Currency
        };

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
            throw new PaymentChargeException(PaymentChargeFailure.Unavailable, exception);
        }
        catch (RpcException exception) when (DateTime.UtcNow >= deadline)
        {
            throw new PaymentChargeException(PaymentChargeFailure.DeadlineExceeded, exception);
        }
        catch (RpcException exception)
        {
            throw new PaymentChargeException(MapFailure(exception.StatusCode), exception);
        }

        if (string.IsNullOrWhiteSpace(response.ChargeId))
        {
            throw new PaymentChargeException(PaymentChargeFailure.UnexpectedResponse);
        }

        return response.Status switch
        {
            ChargeStatus.Approved => new PaymentChargeResult(
                PaymentChargeOutcome.Approved, response.ChargeId),
            ChargeStatus.Declined => new PaymentChargeResult(
                PaymentChargeOutcome.Declined, response.ChargeId),
            _ => throw new PaymentChargeException(PaymentChargeFailure.UnexpectedResponse)
        };
    }

    private static PaymentChargeFailure MapFailure(StatusCode statusCode) => statusCode switch
    {
        StatusCode.Unavailable => PaymentChargeFailure.Unavailable,
        StatusCode.DeadlineExceeded => PaymentChargeFailure.DeadlineExceeded,
        StatusCode.Aborted => PaymentChargeFailure.Pending,
        StatusCode.InvalidArgument => PaymentChargeFailure.InvalidRequest,
        _ => PaymentChargeFailure.UnexpectedResponse
    };
}
