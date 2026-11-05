# Booking dead-letter review (#18 and #19)

The owner of Booking issues #18 and #19 reviews messages in the dead-letter queue (DLQ) of the Booking subscription. This procedure records and routes the outcome; it does not refund a charge, confirm a reservation, or replay a message automatically. Payment processing and the Azure Service Bus consumer remain disabled until the integration is ready.

## Identify the case

1. Use a read-only peek of the Booking subscription DLQ. Record the topic, subscription, `MessageId`, broker dead-letter reason, delivery count and UTC observation time. Compare the reason with Booking's log `ErrorCode` when available; a broker-generated reason such as delivery exhaustion need not match an application error code. Do not receive, complete, delete, resubmit or export the message during initial review.
2. For a valid reservation request, `MessageId` is the request/reservation id. Verify it is a GUID before looking up Booking's reservation by id. An invalid message may have no usable id; leave the id blank rather than inferring it from an unvalidated body.
3. Find Booking logs by `MessageId` and, after successful mapping, by `RequestId` and the `correlationId` from the message body. Broker `CorrelationId` metadata may differ; do not use it as the sole cross-service key. Match the dead-letter reason with the Booking settlement log. If no matching log exists, record the gap without assuming settlement succeeded.
4. Read Booking's persisted status, rejection reason and `ExpiresAt` in UTC. A message in the DLQ is not proof that the reservation is still retained or that Payment charged it.

## Decide the follow-up

| Evidence | Booking review | Follow-up |
|---|---|---|
| `PaymentPending` | Confirm whether the reservation is `Retained` or has become `Expired`. Booking currently makes three attempts and leaves it `Retained` at settlement; final team acceptance of this policy remains open in #19. | The #20 job expires and releases an overdue retention. If the reservation stays `Retained` beyond `ExpiresAt`, raise a #20 defect. Ask the #22 owner to check the Payment row by `reservation_id` if the charge outcome matters. A review within ten minutes is not required to release the seat. |
| `PaymentApprovedAfterExpiry` | Check that Booking did not confirm the reservation and record `Expired` or `Rejected`, plus `ChargeId` only if Payment returned it. | Route the known late charge to the #24 owner for audit review. There is no automatic refund; do not replay the message to confirm the reservation. |
| `PaymentInvalidRequest` or `PaymentUnexpectedResponse` | Check the safe error code and Booking state. The exact settlement for these cases remains local policy until verified with the shared contract. | Coordinate the contract or configuration fault with #22 before considering any replay. |
| Invalid message, `RequestConflict` or business `InvalidRequest` | Check the deterministic reason and whether a reservation exists. | Correct the producer or request contract first. Do not replay unchanged invalid input. |
| Broker exhaustion after a processing or persistence exception | Compare the exception type in Booking logs with the current Booking state; the normal code path propagates these failures for retry. | Determine whether Payment has a row for the same `reservation_id` with #22 before any retry or replay. Do not infer the charge outcome from the delivery count. |

`Unavailable` or `DeadlineExceeded` exhausted produce **Complete** with `Rejected/PaymentUnavailable` if the reservation is eligible, or `Expired` if it expired during the attempts; they normally have no DLQ entry. Find the Booking warning with `ErrorCode=PaymentOutcomeUnknown` and “possible late payment; payment result unknown”; this warning is emitted only after settlement succeeds in either state. If it is absent, check Booking's persisted state and settlement/error logs before classifying the case. The #22 owner checks the Payment table by its unique `reservation_id`; a row and its stored status are needed to establish whether a charge occurred. If an approval is found after Booking rejected or expired the reservation, route it as a late-charge case to #24. An absent or still `Pending` row does not justify recording an approved charge.

## Record the decision safely

Keep one review record per `MessageId` with UTC review time, topic/subscription, dead-letter reason or log `ErrorCode`, `RequestId`/`ReservationId` when validated, body `CorrelationId` when available from a mapped log, `ChargeId` only when returned, Booking status and `ExpiresAt`, Payment status as reported by #22, action taken, owner contacted and review state (`pending`, `escalated`, `resolved`). Record the outcome of the #20 expiry check where relevant. Use a team-approved restricted location for this record; do not put private data in a public issue or PR.

Do not copy message bodies, credentials, service tokens, payment details or connection strings into logs or review records. A replay or DLQ removal needs a separate case-specific decision after checking Booking's current state and Payment's stored result; Booking and Payment currently use the same `reservation_id` as the idempotency key, with final acceptance still to be recorded in #19. The subscription's `MaxDeliveryCount` is not Booking's payment retry policy.
