#!/usr/bin/env bash
set -Eeuo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}"
: "${POSTGRES_DB_NAME:?POSTGRES_DB_NAME must be set}"
: "${POSTGRES_IDENTITY_DB_NAME:?POSTGRES_IDENTITY_DB_NAME must be set}"

create_database_if_missing() {
    local database_name="$1"

    psql \
        --username "$POSTGRES_USER" \
        --dbname postgres \
        --set=ON_ERROR_STOP=1 \
        --set=database_name="$database_name" <<-'EOSQL'
SELECT format('CREATE DATABASE %I', :'database_name')
WHERE NOT EXISTS (
    SELECT 1
    FROM pg_database
    WHERE datname = :'database_name'
) \gexec
EOSQL
}

create_database_if_missing "$POSTGRES_DB_NAME"
create_database_if_missing "$POSTGRES_IDENTITY_DB_NAME"
