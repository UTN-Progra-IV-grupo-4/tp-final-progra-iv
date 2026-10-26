# Seat Reservation System

Group final project — Programming IV

## Overview

A distributed seat reservation system for events (concerts, conferences, shows). The domain is abstract: any event with a limited number of seats where multiple users can attempt to book the same seat simultaneously.

## Reservation States

| State     | Description                                                                 |
|-----------|-----------------------------------------------------------------------------|
| Retained  | Seat is held while payment is processed. Has an expiration timeout.         |
| Confirmed | Payment succeeded. Seat is permanently reserved.                            |
| Rejected  | Reservation failed. Reason recorded: seat taken, payment failed, or payment unavailable. |
| Expired   | Retention time elapsed with no payment result.                              |

A seat is **occupied** while it has a retained or confirmed reservation. Any other state releases the seat.

## Architecture

Microservices style. Three independently deployable services communicate via REST, gRPC, and an async message broker.

```
Client
  │
  ▼
Availability (REST — Clean Architecture)
  │  PUBLISH        SUBSCRIBE
  ▼                     │
Message Broker ──────── │
                         ▼
                   Booking (REST — N-Layer)
                         │  gRPC
                         ▼
                   Payment (gRPC — N-Layer)
                         │
                         ▼
                   Authorization Server
```

## Services

### Availability
- Exposes seat availability queries (public, no auth required)
- Accepts reservation requests and publishes them to the broker
- Delegates reservation status queries to Booking
- **Architecture:** Clean Architecture, anemic domain

### Booking
- Consumes reservation requests from the broker
- Creates retained reservations or rejects them (seat conflict)
- Calls Payment via gRPC to process the charge
- Confirms or rejects based on payment result
- Handles expiration of retained reservations
- **Architecture:** N-Layer, anemic domain

### Payment
- Processes charges for retained reservations via gRPC
- **Architecture:** N-Layer, anemic domain

### Notification Service
- Notifies users when a reservation is created and confirmed after successful payment

### Authorization Server
- Issues tokens for authenticated users
- All inter-service communication is authenticated

## Technical Decisions

Agreed by the team and validated by the professor. Each decision has its own issue with the reasoning and the alternatives.

| Area | Decision | Notes |
|------|----------|-------|
| Platform | .NET 10, single solution (`TpFinal.sln`) | Services never reference each other; only `Contracts` is shared |
| Database | PostgreSQL with Entity Framework Core (Npgsql) | One container, one database per service, each with its own user ([#6](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/6)) |
| Authorization server | Keycloak, running as a container | OIDC for users and client credentials between services ([#5](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/5)) |
| Message broker | Azure Service Bus (one topic, one subscription per consumer), behind an interface | Availability publishes, Booking consumes ([#4](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/4)) |
| Running everything | Docker Compose, one command | Planned in [#3](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/3) |

Secrets, such as the Service Bus connection string, live in a local `.env` file or in `dotnet user-secrets` and are **never committed**: the repository is public. Only `.env.example` with empty values is versioned.

## Reservation Flow

1. **Query** — Any visitor queries available seats. No login required.
2. **Auth** — User obtains a token from the Authorization Server.
3. **Request** — User sends a reservation request to Availability. Availability validates the seat, registers the request with a unique ID, and publishes it to the broker. Responds immediately (async).
4. **Processing** — A Booking replica picks up the message. Creates a retained reservation if the seat is free, rejects otherwise.
5. **Payment** — Booking calls Payment via gRPC to charge the retained reservation.
6. **Result** — Booking confirms or rejects based on Payment's response.
7. **Status check** — User polls Availability → Availability queries Booking.
8. **Expiration** — If a retained reservation reaches its timeout with no result, it expires and the seat is released.

## Workflow

Tasks are tracked as GitHub Issues on the [project board](https://github.com/orgs/UTN-Progra-IV-grupo-4/projects/1) (Backlog → Todo → In Progress → Blocked → Review → Develop → Prod).

### Branches

Branches are named after the issue number:

```
main                        → stable, production-ready
develop                     → integration branch
<issue-number>-short-name   → one branch per issue, created from develop (e.g. 12-availability-base)
```

Nobody pushes directly to `main` or `develop`: both are protected by a ruleset.

### Pull requests

- Open the PR from `<issue-number>-short-name` into `develop`.
- The description must include `Closes #<issue-number>` (the PR template already has it).
- At least **1 approval** from another team member is required, so nobody merges their own PR.
- Releases merge `develop` → `main`, and the cards are moved to **Prod** by hand.

### Commits

[Conventional Commits](https://www.conventionalcommits.org/): `type(scope): description`, for example `feat(booking): consume reservation requests`.
Allowed types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `build`, `ci`.

## Glossary

| Term    | Definition                              |
|---------|-----------------------------------------|
| Event   | The occasion with seats to reserve      |
| Message | What travels through the broker         |
| Suceso  | Actions within system components        |
| Seat    | Physical place to sit                   |
| Booking | The rental of a physical seat           |
| Payment | Finalization of a physical seat rental  |
