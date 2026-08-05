#!/usr/bin/env bash
set -Eeuo pipefail

secret_file="/run/secrets/postgres_password"
management_secret_file="/run/secrets/wildfly_management_password"
management_user="${WILDFLY_MANAGEMENT_USER:-uta-console-admin}"

if [[ ! -s "${secret_file}" ]]; then
  echo "ERROR: no se encontró el secreto PostgreSQL." >&2
  exit 1
fi

export FIRMAEC_DB_PASSWORD
FIRMAEC_DB_PASSWORD="$(<"${secret_file}")"

if [[ -s "${management_secret_file}" ]]; then
  /opt/jboss/wildfly/bin/add-user.sh \
    -u "${management_user}" \
    -p "$(<"${management_secret_file}")" \
    --silent
fi

exec /opt/jboss/wildfly/bin/standalone.sh \
  -b 0.0.0.0 \
  -bmanagement 0.0.0.0 \
  "$@"
