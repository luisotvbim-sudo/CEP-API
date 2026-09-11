#!/bin/sh
set -eu
# Only runs on a fresh volume. Passwords are passed as psql variables, never SQL-concatenated.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=owner_password="$(cat /run/secrets/owner_password)" \
  --set=runtime_password="$(cat /run/secrets/runtime_password)" <<'SQL'
CREATE ROLE cep_api_owner LOGIN PASSWORD :'owner_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE ROLE cep_api_runtime LOGIN PASSWORD :'runtime_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
ALTER DATABASE cep_api OWNER TO cep_api_owner;
REVOKE ALL ON DATABASE cep_api FROM PUBLIC;
GRANT CONNECT ON DATABASE cep_api TO cep_api_runtime;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO cep_api_owner;
GRANT USAGE ON SCHEMA public TO cep_api_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE cep_api_owner IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO cep_api_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE cep_api_owner IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO cep_api_runtime;
SQL
