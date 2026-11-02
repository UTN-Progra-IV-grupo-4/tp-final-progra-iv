# Booking payment processing

This guide describes the Booking flow for [issue #19](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/19). Business processes payment through an internal port, Data applies conditional reservation transitions, and the consumer can continue payment after creating or finding a compatible `Retained` reservation. The gRPC adapter is still pending. Both the broker and payment continuation are disabled by default.

## Flow

For a retained reservation, Booking will call Payment through the shared gRPC contract, using `Reservation.Id` as the payment idempotency key. Booking owns the reservation state; Payment owns the charge and its idempotent result.

| Payment result | Booking state |
| --- | --- |
| Approved, while the reservation remains `Retained` and `ExpiresAt` is in the future | `Confirmed` |
| Declined | `Rejected`, reason `PaymentFailed` |
| Payment unavailable after bounded retries | `Rejected`, reason `PaymentUnavailable`, while the reservation is still eligible for a conditional transition |

The result must be persisted before Booking completes the broker message. A compatible redelivery should continue a reservation that is still `Retained` and unexpired; a terminal or expired reservation should not start another charge. State changes must be conditional on both status and expiration so a late response cannot confirm a reservation whose seat has been released. Issue #19 provisionally records a late or otherwise unpaired charge in audit logs (#24), without an automatic refund; the team must explicitly accept this limitation.

## Contract and configuration

The candidate contract on the `22-payment-grpc` branch defines `PaymentService.Charge` with `reservation_id` (`string`), `amount_cents` (`int64`), and `currency` (`string`). Its response has approved, declined, and unspecified statuses. This contract is not yet in `develop`, so Booking must not introduce a parallel `.proto`.

Issues #19 and #22 define one fixed price per seat in integer cents: Booking configures `Payment:AmountCents` and sends it as `amount_cents` without conversion. The default placeholder is zero, so a valid price must be configured before enabling payment processing. The candidate `.proto` must still be checked when #22 is merged. `Payment:Currency` is a three-uppercase-letter ISO 4217 string; #22 provisionally accepts only `ARS` through a configurable `SupportedCurrencies` list. A repeated `reservation_id` with different amount or currency should produce `INVALID_ARGUMENT` under #22's provisional rule. The Booking → Payment call will carry a service token (#11) and propagate `CorrelationId` in gRPC metadata (#24); those cross-service mechanisms need coordinated integration. Keep credentials and environment specific addresses outside the repository.

Issue #19 provisionally proposes a configurable five-second gRPC deadline. The Business processor currently defaults to three calls and a one-second delay, both configurable; it checks reservation eligibility before each call. The gRPC deadline and endpoint remain to be implemented in the adapter. A local three-letter syntax check cannot replace Payment's supported-currency validation. These values come from the project issues, not the course example.

## Verification and open decisions

Business logic and consumer settlement are tested with a fake Payment client before the shared contract and server are ready. PostgreSQL integration tests cover conditional confirmation and expiration; duplicate delivery and persistence failure are covered locally with the fake, with cross-service integration still pending. Once the contract is merged, test the gRPC adapter against a local test server; repeat with the real Payment service when #22 is available. The demo in #25 also needs the approved path and Payment absent → `PaymentUnavailable`.

Issue #22 specifies Payment's `SimulatedGateway:Mode` to select simulated outcomes independently of the fixed price. `Approve`, `Decline`, `Slow`, and `Error` are needed; `Expire` appears in the task list but is optional and not part of the demo in the decision text. Expiration should be tested using `TimeProvider` with the official ten-minute retention. The gateway and retry behavior still need implementation and verification.

Under the provisional rules in #19/#22, `Pending` returns `ABORTED` and retries with the same `reservation_id`. `Unavailable` and `DeadlineExceeded` also trigger bounded retries with that key; only after attempts are exhausted does Booking persist `PaymentUnavailable` if the reservation remains eligible. This retry policy is implemented by Booking Business and does not depend on the subscription's `MaxDeliveryCount`. For local verification, exhausted `Pending`, invalid Payment requests, unexpected Payment responses and known late approvals go to dead-letter with safe reasons for manual review; the team still needs to agree on these provisional decisions. An observed late `APPROVED` is reported as `LateApproved`; distributed audit logging is pending #24. After only failed calls, Booking logs that the payment result is unknown until it can correlate with Payment. The team's acceptance of late-charge handling without refund remains open. The candidate contract has no charge lookup or reversal operation.
