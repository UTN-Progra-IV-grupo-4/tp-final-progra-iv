# Keycloak - Authorization Server

Keycloak manages authentication and authorization for the seat reservation system using OpenID Connect (OIDC) and OAuth 2.0.

## Configuration

### Realm: `seat-reservation`

**Clients:**

| Client | Type | Flow | Purpose | Audience Mapper |
|--------|------|-------|---------|-----------------|
| `availability-web` | Public | Authorization Code + PKCE | User authentication in Availability.Web | N/A |
| `availability-service` | Confidential | Client Credentials | Availability calls Booking | `booking-service` |
| `booking-service` | Confidential | Client Credentials | Booking calls Payment | `payment-service` |
| `payment-service` | Confidential | Token validation | Payment validates tokens | N/A |

**Secrets** use placeholders `${VAR}` resolved from environment variables during realm import.

**Test users** (development only, passwords in cleartext):
- `user1` / `User123!`
- `user2` / `User123!`

**Identity provider:** Google (disabled until `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET` are configured)

## Quick Start

```bash
# 1. Copy and fill .env
cp .env.example .env
# Edit .env to set KEYCLOAK_ADMIN, KEYCLOAK_ADMIN_PASSWORD, and all *_SECRET variables

# 2. Start Keycloak
docker compose up keycloak -d

# 3. Access admin console
# URL: http://localhost:8080/admin
# Login with credentials from .env (KEYCLOAK_ADMIN / KEYCLOAK_ADMIN_PASSWORD)
```

## Test Client Credentials

```bash
# Get token for booking-service
curl -X POST http://localhost:8080/realms/seat-reservation/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=booking-service" \
  -d "client_secret=<KEYCLOAK_BOOKING_CLIENT_SECRET from .env>"
```

The returned JWT should include `aud: ["payment-service"]` (added by the audience mapper). Decode at https://jwt.io to verify.

## Environment Variables

Required in `.env`:

```bash
KEYCLOAK_ADMIN=admin
KEYCLOAK_ADMIN_PASSWORD=admin
KEYCLOAK_PORT=8080
KEYCLOAK_REALM=seat-reservation
KEYCLOAK_ISSUER=http://localhost:8080/realms/seat-reservation
KEYCLOAK_FRONTEND_CLIENT_ID=availability-web
KEYCLOAK_AVAILABILITY_CLIENT_ID=availability-service
KEYCLOAK_AVAILABILITY_CLIENT_SECRET=<generate-secure-secret>
KEYCLOAK_BOOKING_CLIENT_ID=booking-service
KEYCLOAK_BOOKING_CLIENT_SECRET=<generate-secure-secret>
KEYCLOAK_PAYMENT_CLIENT_ID=payment-service
KEYCLOAK_PAYMENT_CLIENT_SECRET=<generate-secure-secret>
GOOGLE_CLIENT_ID=
GOOGLE_CLIENT_SECRET=
```

**⚠️ Important:**
- Generate secure secrets for `*_CLIENT_SECRET` (e.g., `openssl rand -base64 32`)
- Never commit `.env` to the repository
- Google IDP is optional; local users work without it

## Troubleshooting

### Container fails to start

If a secret variable is empty or undefined, Docker Compose will fail to start (using `${VAR:?}` syntax). Check that all `KEYCLOAK_*_CLIENT_SECRET` variables are set in `.env`.

### Placeholders not resolved

If realm imports but secrets remain as literal `${...}` text:
1. Verify environment variables are set in `.env`
2. Verify docker-compose.yml passes them to the container
3. Restart: `docker compose down && docker compose up keycloak -d`

### Token validation fails

- Verify `KEYCLOAK_ISSUER` matches Keycloak URL
- Verify token is not expired
- Verify audience matches the target service
- Verify client secret matches between service and Keycloak

## Architecture Decisions

**Why Keycloak in Docker:**
- Reproducible: anyone gets the same config with `docker compose up`
- No external dependencies: works offline, no Azure account needed
- Free and open source
- Validated by professor for this TP

**Why Authorization Code + PKCE for users:**
- PKCE prevents authorization code interception attacks
- Standard for public clients in OAuth 2.1
- Works well with modern web apps

**Why Client Credentials for services:**
- Services have their own identity, don't reuse user tokens
- Secrets are confidential and can be rotated
- Required by the TP specification

## Endpoints

| Endpoint | Purpose |
|----------|---------|
| `http://localhost:8080/realms/seat-reservation/.well-known/openid-configuration` | OIDC discovery |
| `http://localhost:8080/realms/seat-reservation/protocol/openid-connect/token` | Token endpoint |
| `http://localhost:8080/admin` | Admin console |

## References

- [Keycloak Documentation](https://www.keycloak.org/documentation)
- [Issue #5 - Authorization server decision](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/5)
- [Issue #10 - Configure authorization server](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/10)
