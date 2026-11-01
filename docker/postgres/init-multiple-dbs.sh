#!/usr/bin/env bash
set -euo pipefail

required_vars=(
  AVAILABILITY_DB_NAME AVAILABILITY_DB_USER AVAILABILITY_DB_PASSWORD
  BOOKING_DB_NAME BOOKING_DB_USER BOOKING_DB_PASSWORD
  PAYMENT_DB_NAME PAYMENT_DB_USER PAYMENT_DB_PASSWORD
)

for var_name in "${required_vars[@]}"; do
  if [ -z "${!var_name:-}" ]; then
    echo "Error: variable ${var_name} is required." >&2
    exit 1
  fi
done

create_db_and_user() {
  local db_name="$1"
  local db_user="$2"
  local db_password="$3"

  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-EOSQL
    DO
    \$\$
    BEGIN
      IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '${db_user}') THEN
        EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', '${db_user}', '${db_password}');
      END IF;
    END
    \$\$;

    SELECT format('CREATE DATABASE %I OWNER %I', '${db_name}', '${db_user}')
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '${db_name}')
    \gexec

    REVOKE CONNECT ON DATABASE "${db_name}" FROM PUBLIC;
    GRANT CONNECT ON DATABASE "${db_name}" TO "${db_user}";
EOSQL
}

create_db_and_user "$AVAILABILITY_DB_NAME" "$AVAILABILITY_DB_USER" "$AVAILABILITY_DB_PASSWORD"
create_db_and_user "$BOOKING_DB_NAME" "$BOOKING_DB_USER" "$BOOKING_DB_PASSWORD"
create_db_and_user "$PAYMENT_DB_NAME" "$PAYMENT_DB_USER" "$PAYMENT_DB_PASSWORD"

echo "PostgreSQL initialization completed for availability, booking and payment databases."
