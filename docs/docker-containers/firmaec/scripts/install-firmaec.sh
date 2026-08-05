#!/usr/bin/env bash
set -Eeuo pipefail

INSTALL_DIR="${UTA_CONTAINERS_DIR:-/opt/uta-containers/electronic-signature}"
DATA_DIR="${FIRMAEC_DATA_DIR:-/var/lib/uta-containers/firmaec-postgresql}"
BACKUP_DIR="${FIRMAEC_BACKUP_DIR:-/var/backups/uta-containers/firmaec-postgresql}"
PGJDBC_VERSION="42.7.13"
PGJDBC_SHA256="6e0e4cc2d8cae902084f8a2b18728b073a6fd9d1f87c9d8bff8f298c18185b93"
PGJDBC_URL="https://repo1.maven.org/maven2/org/postgresql/postgresql/${PGJDBC_VERSION}/postgresql-${PGJDBC_VERSION}.jar"
SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

log() {
  printf '\n[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"
}

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "no se encontró el comando '$1'."
}

compose() {
  docker compose --env-file "${INSTALL_DIR}/.env" -f "${INSTALL_DIR}/compose.yaml" "$@"
}

wait_for_health() {
  local container="$1"
  local attempts="$2"
  local state

  for ((i=1; i<=attempts; i++)); do
    state="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "${container}" 2>/dev/null || true)"
    printf '%s: %s (%d/%d)\n' "${container}" "${state:-no disponible}" "${i}" "${attempts}"
    [[ "${state}" == "healthy" ]] && return 0
    [[ "${state}" == "unhealthy" ]] && return 1
    sleep 5
  done

  return 1
}

validate_deployments() {
  local deployment output

  for deployment in api.war servicio.war; do
    output="$(
      docker exec firmaec-wildfly \
        /opt/jboss/wildfly/bin/jboss-cli.sh \
        --connect \
        --command="/deployment=${deployment}:read-attribute(name=status)" \
        2>&1
    )"
    grep -Eq '"result"[[:space:]]*=>[[:space:]]*"OK"' <<<"${output}" \
      || {
        echo "${output}" >&2
        fail "el despliegue ${deployment} no está en estado OK."
      }
  done
}

prepare_files() {
  [[ "$(uname -m)" == "x86_64" ]] || fail "se requiere arquitectura amd64/x86_64."
  [[ -f "${SOURCE_DIR}/artifacts/api.war" ]] || fail "falta artifacts/api.war."
  [[ -f "${SOURCE_DIR}/artifacts/servicio.war" ]] || fail "falta artifacts/servicio.war."

  log "Creando estructura institucional"
  sudo install -d -o root -g docker -m 0775 \
    /opt/uta-containers \
    "${INSTALL_DIR}" \
    "${BACKUP_DIR}"

  if [[ "${SOURCE_DIR}" != "${INSTALL_DIR}" ]]; then
    sudo cp -a "${SOURCE_DIR}/." "${INSTALL_DIR}/"
  fi

  sudo chown -R root:docker "${INSTALL_DIR}"
  sudo find "${INSTALL_DIR}" -type d -exec chmod 0775 {} +
  sudo find "${INSTALL_DIR}" -type f -exec chmod 0664 {} +
  sudo chmod 0755 "${INSTALL_DIR}"/scripts/*.sh "${INSTALL_DIR}/wildfly/entrypoint.sh"

  if [[ ! -f "${INSTALL_DIR}/.env" ]]; then
    sudo cp "${INSTALL_DIR}/.env.example" "${INSTALL_DIR}/.env"
  fi
  # Este archivo contiene únicamente valores no secretos; debe ser legible
  # por el operador que ejecuta Docker Compose.
  sudo chmod 0644 "${INSTALL_DIR}/.env"

  sudo install -d -o root -g docker -m 0750 "${INSTALL_DIR}/secrets"
  for secret in postgres_password firmaec_client_api_key firmaec_callback_api_key wildfly_management_password; do
    if [[ ! -s "${INSTALL_DIR}/secrets/${secret}" ]]; then
      local temp_secret
      temp_secret="$(mktemp)"
      openssl rand -hex 32 > "${temp_secret}"
      sudo install -o root -g docker -m 0640 \
        "${temp_secret}" "${INSTALL_DIR}/secrets/${secret}"
      rm -f "${temp_secret}"
    fi
  done

  # Los secretos consumidos por WildFly se montan como archivos bind por
  # Docker Compose. El UID 1000 corresponde al usuario jboss de la imagen
  # oficial; modo 0400 evita lectura por otros usuarios del host/contenedor.
  sudo chown 1000:root \
    "${INSTALL_DIR}/secrets/postgres_password" \
    "${INSTALL_DIR}/secrets/wildfly_management_password"
  sudo chmod 0400 \
    "${INSTALL_DIR}/secrets/postgres_password" \
    "${INSTALL_DIR}/secrets/wildfly_management_password"

  sudo chown root:docker \
    "${INSTALL_DIR}/secrets/firmaec_client_api_key" \
    "${INSTALL_DIR}/secrets/firmaec_callback_api_key"
  sudo chmod 0640 \
    "${INSTALL_DIR}/secrets/firmaec_client_api_key" \
    "${INSTALL_DIR}/secrets/firmaec_callback_api_key"

  # signature_database_connection y repositoryuta_service_client_secret son de
  # signature-api y NO se generan al azar (a diferencia de los de arriba): son
  # la cadena de conexión SQL Server real y el secreto real del cliente
  # registrado en RepositoryUta. Si faltan, se avisa aquí en vez de levantar
  # signature-api con un secreto vacío/incorrecto.
  for secret in signature_database_connection repositoryuta_service_client_secret; do
    if [[ ! -s "${INSTALL_DIR}/secrets/${secret}" ]]; then
      echo "AVISO: falta '${INSTALL_DIR}/secrets/${secret}' (valor real, no autogenerable)." >&2
      echo "       signature-api no arrancará bien hasta colocarlo (permisos 0640 root:docker)." >&2
    fi
  done
}

download_driver() {
  local destination="${INSTALL_DIR}/artifacts/postgresql-${PGJDBC_VERSION}.jar"
  local temp_driver

  if [[ -f "${destination}" ]] \
    && echo "${PGJDBC_SHA256}  ${destination}" | sha256sum -c - >/dev/null 2>&1; then
    log "Driver JDBC ${PGJDBC_VERSION} ya verificado"
    return
  fi

  log "Descargando pgJDBC ${PGJDBC_VERSION}"
  temp_driver="$(mktemp)"
  curl -fsSL --retry 3 --connect-timeout 15 \
    "${PGJDBC_URL}" -o "${temp_driver}"
  echo "${PGJDBC_SHA256}  ${temp_driver}" | sha256sum -c -
  sudo install -o root -g docker -m 0644 "${temp_driver}" "${destination}"
  rm -f "${temp_driver}"
}

prepare_postgres_data() {
  local postgres_image postgres_uid postgres_gid
  postgres_image="$(awk -F= '$1=="POSTGRES_IMAGE"{print $2}' "${INSTALL_DIR}/.env")"
  postgres_uid="$(docker run --rm --entrypoint id "${postgres_image}" -u postgres)"
  postgres_gid="$(docker run --rm --entrypoint id "${postgres_image}" -g postgres)"

  log "Preparando persistencia PostgreSQL con UID ${postgres_uid}"
  sudo install -d -o "${postgres_uid}" -g "${postgres_gid}" -m 0700 "${DATA_DIR}"
}

install_stack() {
  require_command docker
  require_command curl
  require_command openssl
  require_command sha256sum
  docker compose version >/dev/null

  log "Habilitando Docker"
  sudo systemctl enable --now docker

  prepare_files
  cd "${INSTALL_DIR}"

  log "Descargando imágenes oficiales"
  docker pull postgres:17.10-bookworm
  docker pull quay.io/wildfly/wildfly:40.0.1.Final-jdk17

  download_driver
  prepare_postgres_data

  log "Validando Docker Compose"
  compose config --quiet

  log "Construyendo imagen institucional WildFly"
  compose build firmaec-wildfly

  log "Levantando PostgreSQL"
  compose up -d firmaec-postgresql
  wait_for_health firmaec-postgresql 24 \
    || fail "PostgreSQL no alcanzó estado healthy."

  log "Levantando WildFly y desplegando FirmaEC"
  compose up -d firmaec-wildfly
  wait_for_health firmaec-wildfly 36 \
    || {
      compose logs --tail=200 firmaec-wildfly
      fail "WildFly no alcanzó estado healthy."
    }

  log "Validación final"
  validate_deployments
  compose ps
  docker stats --no-stream firmaec-wildfly firmaec-postgresql
  docker exec firmaec-wildfly \
    /opt/jboss/wildfly/bin/jboss-cli.sh --connect --command=deployment-info

  # signature-api NO se construye aquí: su build.context es la raíz del repo
  # completo (src/), que este script no copia (solo copia docs/docker-containers/
  # firmaec/). Se asume que la imagen ya existe localmente — construida con
  # scripts/build-signature-api.sh desde una release completa, o cargada con
  # import-images/import-firmaec.sh — y aquí solo se levanta el contenedor.
  if docker image inspect "$(awk -F= '$1=="SIGNATURE_API_IMAGE"{print $2}' "${INSTALL_DIR}/.env")" >/dev/null 2>&1; then
    log "Levantando signature-api"
    compose up -d signature-api
    wait_for_health signature-api 12 \
      || {
        compose logs --tail=200 signature-api
        fail "signature-api no alcanzó estado healthy."
      }
    docker stats --no-stream signature-api
  else
    echo
    echo "AVISO: la imagen signature-api todavía no existe localmente — no se levantó."
    echo "       Constrúyala con scripts/build-signature-api.sh (requiere el repo completo)"
    echo "       o cárguela con 'import-images'/'import-firmaec.sh', y luego:"
    echo "         ./scripts/install-firmaec.sh signature-api-start"
  fi

  echo
  echo "FirmaEC quedó accesible solamente en http://127.0.0.1:8180"
  echo "Apache y react-nginx-app no fueron modificados."
}

export_images() {
  local output="${2:-${PWD}/firmaec-images-$(date -u +%Y%m%dT%H%M%SZ).tar.gz}"
  local wildfly_image postgres_image signature_api_image
  wildfly_image="$(awk -F= '$1=="FIRMAEC_IMAGE"{print $2}' "${INSTALL_DIR}/.env")"
  postgres_image="$(awk -F= '$1=="POSTGRES_IMAGE"{print $2}' "${INSTALL_DIR}/.env")"
  signature_api_image="$(awk -F= '$1=="SIGNATURE_API_IMAGE"{print $2}' "${INSTALL_DIR}/.env")"
  # signature-api debe existir ya en "docker images" (constrúyala primero con
  # scripts/build-signature-api.sh) — este comando solo empaqueta, no construye.
  docker image save "${wildfly_image}" "${postgres_image}" "${signature_api_image}" | gzip -9 > "${output}"
  sha256sum "${output}" > "${output}.sha256"
  echo "Imágenes exportadas en ${output}"
}

import_images() {
  local input="${2:-}"
  [[ -n "${input}" && -f "${input}" ]] || fail "indique el archivo .tar.gz de imágenes."
  [[ -f "${input}.sha256" ]] && sha256sum -c "${input}.sha256"
  gzip -dc "${input}" | docker image load
}

show_transfer() {
  cat <<'EOF'
Migración a otro servidor (los tres contenedores: WildFly, PostgreSQL y
signature-api):

0. Si signature-api tiene cambios de código sin construir, constrúyala
   primero desde una release completa del repo:
   ./scripts/build-signature-api.sh [/ruta/a/release/completa]

1. Exportar configuración, imágenes (las tres) y respaldo PostgreSQL:
   /opt/uta-containers/electronic-signature/scripts/export-firmaec.sh

2. Copiar el paquete:
   scp firmaec-FECHA.tar.gz firmaec-FECHA.tar.gz.sha256 usuario@SERVIDOR:/tmp/

3. En el servidor nuevo, cargar las imágenes:
   ./scripts/import-firmaec.sh /tmp/firmaec-FECHA.tar.gz

4. Transferir secretos por un canal institucional seguro — incluye
   signature_database_connection y repositoryuta_service_client_secret,
   propios de signature-api (no se generan al azar).

5. Instalar:
   ./scripts/install-firmaec.sh install

6. Restaurar PostgreSQL únicamente después de validar que la base destino
   está vacía y que existe un respaldo recuperable.
EOF
}

show_console() {
  cat <<'EOF'
Acceso operativo:

  cd /opt/uta-containers/electronic-signature
  tmux attach -t firmaec
  ./scripts/install-firmaec.sh status
  ./scripts/install-firmaec.sh wildfly-cli
  ./scripts/install-firmaec.sh wildfly-shell
  ./scripts/install-firmaec.sh postgres-shell
  ./scripts/install-firmaec.sh console-credentials

Dentro de JBoss CLI:

  deployment-info
  /subsystem=datasources/data-source=FirmaDigitalDS:test-connection-in-pool
  :read-attribute(name=server-state)
  quit

No se publica la consola web administrativa 9990.
EOF
}

show_console_credentials() {
  local user
  user="$(
    awk -F= '$1=="WILDFLY_MANAGEMENT_USER"{print $2}' \
      "${INSTALL_DIR}/.env"
  )"
  echo "URL: https://portal.uta.edu.ec/firmaec-console/"
  echo "Usuario: ${user:-uta-console-admin}"
  printf 'Contraseña: '
  sudo cat "${INSTALL_DIR}/secrets/wildfly_management_password"
}

usage() {
  cat <<'EOF'
Uso: install-firmaec.sh COMANDO

  install                    Instala, construye y levanta los dos contenedores.
  pull                       Descarga las imágenes base.
  build                      Construye la imagen WildFly institucional.
  start|stop|restart         Gestiona ambos contenedores.
  status|logs                Consulta estado o logs.
  memory-status              Muestra la distribución y consumo de memoria.
  docker-start               Habilita e inicia Docker.
  postgres-start             Levanta PostgreSQL.
  postgres-stop              Detiene PostgreSQL.
  postgres-restart           Reinicia PostgreSQL.
  postgres-status            Comprueba PostgreSQL.
  wildfly-start              Levanta WildFly.
  wildfly-stop               Detiene WildFly.
  wildfly-restart            Reinicia WildFly.
  wildfly-status             Lista los WAR desplegados.
  wildfly-cli                Abre la consola JBoss CLI.
  wildfly-shell              Abre Bash dentro de WildFly.
  signature-api-start/stop/restart/status/logs/shell  Gestiona signature-api.
  signature-api-build        Explica cómo construir signature-api (repo completo).
  console-help               Muestra rutas y comandos de consola.
  console-credentials        Muestra las credenciales protegidas.
  console-enable [IP] [MIN]  Publica temporalmente la consola por HTTPS.
  console-status             Muestra el estado de la consola temporal.
  console-disable            Despublica inmediatamente la consola.
  proxy-install              Publica /firmaec/ mediante Apache HTTPS.
  proxy-status               Muestra y comprueba el proxy FirmaEC.
  proxy-remove               Retira únicamente el bloque proxy administrado.
  export-images [archivo]    Genera un archivo transportable de imágenes.
  import-images ARCHIVO      Carga imágenes en otro servidor.
  export-bundle [directorio] Exporta imágenes, configuración y PostgreSQL.
  transfer-help              Muestra el procedimiento de migración.
EOF
}

command="${1:-help}"

case "${command}" in
  install)
    install_stack
    ;;
  pull|build|start|stop|restart|status|memory-status|logs|docker-start|docker-status|\
postgres-start|postgres-stop|postgres-restart|postgres-status|postgres-shell|\
wildfly-start|wildfly-stop|wildfly-restart|wildfly-status|wildfly-logs|\
wildfly-cli|wildfly-shell|\
signature-api-start|signature-api-stop|signature-api-restart|signature-api-status|\
signature-api-logs|signature-api-shell|signature-api-build)
    exec "${INSTALL_DIR}/scripts/manage-firmaec.sh" "${command}"
    ;;
  console-help)
    show_console
    ;;
  console-credentials)
    show_console_credentials
    ;;
  console-enable)
    exec sudo "${INSTALL_DIR}/apache/configure-wildfly-console.sh" \
      enable "${2:-}" "${3:-120}"
    ;;
  console-status)
    exec "${INSTALL_DIR}/apache/configure-wildfly-console.sh" status
    ;;
  console-disable)
    exec sudo "${INSTALL_DIR}/apache/configure-wildfly-console.sh" disable
    ;;
  proxy-install)
    exec sudo "${INSTALL_DIR}/apache/configure-apache-proxy.sh" install
    ;;
  proxy-status)
    exec "${INSTALL_DIR}/apache/configure-apache-proxy.sh" status
    ;;
  proxy-remove)
    exec sudo "${INSTALL_DIR}/apache/configure-apache-proxy.sh" remove
    ;;
  export-images)
    export_images "$@"
    ;;
  import-images)
    import_images "$@"
    ;;
  export-bundle)
    exec "${INSTALL_DIR}/scripts/export-firmaec.sh" "${2:-}"
    ;;
  transfer-help)
    show_transfer
    ;;
  help|-h|--help)
    usage
    ;;
  *)
    fail "comando no reconocido: ${command}"
    ;;
esac
