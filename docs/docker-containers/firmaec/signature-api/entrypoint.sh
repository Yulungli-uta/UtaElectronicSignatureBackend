#!/bin/sh
set -eu

load_secret() {
  variable_name="$1"
  secret_path="$2"
  if [ ! -s "$secret_path" ]; then
    echo "ERROR: falta el secreto requerido $secret_path." >&2
    exit 1
  fi
  secret_value="$(cat "$secret_path")"
  export "$variable_name=$secret_value"
  unset secret_value
}

load_secret \
  "ConnectionStrings__SignatureDatabase" \
  "/run/secrets/signature_database_connection"
load_secret \
  "RepositoryUta__ServiceClientSecret" \
  "/run/secrets/repositoryuta_service_client_secret"
load_secret \
  "FirmaEc__ApiKey" \
  "/run/secrets/firmaec_client_api_key"
load_secret \
  "FirmaEc__CallbackApiKey" \
  "/run/secrets/firmaec_callback_api_key"

exec dotnet UtaElectronicSignature.API.dll "$@"
