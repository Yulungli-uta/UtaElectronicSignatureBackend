#!/usr/bin/env bash
set -Eeuo pipefail

cli="/opt/jboss/wildfly/bin/jboss-cli.sh"

for deployment in api.war servicio.war; do
  output="$(
    "${cli}" \
      --connect \
      --command="/deployment=${deployment}:read-attribute(name=status)" \
      2>/dev/null
  )"
  grep -Eq '"result"[[:space:]]*=>[[:space:]]*"OK"' <<<"${output}" || exit 1
done
