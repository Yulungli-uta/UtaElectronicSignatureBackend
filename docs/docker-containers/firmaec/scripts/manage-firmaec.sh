#!/usr/bin/env bash
set -Eeuo pipefail

INSTALL_DIR="${UTA_CONTAINERS_DIR:-/opt/uta-containers/electronic-signature}"
cd "${INSTALL_DIR}"

compose() {
  # -f compose.yaml explícito desactivaba la fusión automática de Compose con
  # compose.override.yaml (ej. el CORS temporal para localhost:5173, o los
  # puertos de firmaec-wildfly) — cualquier recreación via este script la
  # perdía en silencio. Se agrega el override explícitamente solo si existe,
  # así el comportamiento no cambia una vez que se elimine antes de producción.
  local files=(-f compose.yaml)
  if [ -f compose.override.yaml ]; then
    files+=(-f compose.override.yaml)
  fi
  docker compose --env-file .env "${files[@]}" "$@"
}

usage() {
  cat <<'EOF'
Uso: manage-firmaec.sh COMANDO

Docker:
  docker-start       Habilita e inicia Docker.
  docker-status      Muestra el estado de Docker.

Contenedores:
  start              Levanta PostgreSQL, WildFly y signature-api.
  stop               Detiene los tres contenedores.
  restart            Reinicia los tres contenedores.
  status             Muestra estado, health y recursos.
  memory-status      Muestra memoria del host, límites y consumo.
  logs               Sigue logs de los tres contenedores.

PostgreSQL:
  postgres-start     Levanta únicamente PostgreSQL.
  postgres-stop      Detiene únicamente PostgreSQL.
  postgres-restart   Reinicia PostgreSQL.
  postgres-status    Ejecuta pg_isready.
  postgres-shell     Abre psql de forma interactiva.

WildFly:
  wildfly-start      Levanta WildFly y su dependencia PostgreSQL.
  wildfly-stop       Detiene WildFly.
  wildfly-restart    Reinicia WildFly.
  wildfly-status     Lista despliegues mediante JBoss CLI.
  wildfly-logs       Sigue solamente los logs de WildFly.
  wildfly-cli        Abre la consola JBoss CLI.
  wildfly-shell      Abre una terminal Bash dentro de WildFly.

signature-api (backend .NET):
  signature-api-start    Levanta signature-api (y su dependencia WildFly).
  signature-api-stop     Detiene signature-api.
  signature-api-restart  Reinicia signature-api.
  signature-api-status   Comprueba /health/live y el estado del contenedor.
  signature-api-logs     Sigue solamente los logs de signature-api.
  signature-api-shell    Abre una terminal Bash dentro de signature-api.
  signature-api-build    Explica cómo construir la imagen (requiere el repo
                          completo — no se puede construir desde este
                          directorio; ver scripts/build-signature-api.sh).

Imágenes:
  pull               Descarga las imágenes base (WildFly y PostgreSQL).
  build              Construye la imagen institucional WildFly.
EOF
}

command="${1:-help}"

case "${command}" in
  docker-start)
    sudo systemctl enable --now docker
    ;;
  docker-status)
    systemctl --no-pager --full status docker
    ;;
  start)
    compose up -d
    ;;
  stop)
    compose stop
    ;;
  restart)
    compose restart
    ;;
  status)
    compose ps
    docker stats --no-stream firmaec-wildfly firmaec-postgresql signature-api
    ;;
  memory-status)
    free -h
    echo
    docker stats --no-stream \
      --format 'table {{.Name}}\t{{.MemUsage}}\t{{.MemPerc}}\t{{.PIDs}}'
    echo
    for container in firmaec-wildfly firmaec-postgresql signature-api; do
      docker inspect \
        -f '{{.Name}} limit={{.HostConfig.Memory}} reservation={{.HostConfig.MemoryReservation}} swap_total={{.HostConfig.MemorySwap}} oom_killed={{.State.OOMKilled}}' \
        "${container}"
    done
    ;;
  logs)
    compose logs -f --tail=200
    ;;
  postgres-start)
    compose up -d firmaec-postgresql
    ;;
  postgres-stop)
    compose stop firmaec-postgresql
    ;;
  postgres-restart)
    compose restart firmaec-postgresql
    ;;
  postgres-status)
    docker exec firmaec-postgresql pg_isready -U firmadigital -d firmadigital
    ;;
  postgres-shell)
    docker exec -it firmaec-postgresql psql -U firmadigital -d firmadigital
    ;;
  wildfly-start)
    compose up -d firmaec-wildfly
    ;;
  wildfly-stop)
    compose stop firmaec-wildfly
    ;;
  wildfly-restart)
    compose restart firmaec-wildfly
    ;;
  wildfly-status)
    docker exec firmaec-wildfly \
      /opt/jboss/wildfly/bin/jboss-cli.sh --connect --command=deployment-info
    ;;
  wildfly-logs)
    compose logs -f --tail=200 firmaec-wildfly
    ;;
  wildfly-cli)
    docker exec -it firmaec-wildfly \
      /opt/jboss/wildfly/bin/jboss-cli.sh --connect
    ;;
  wildfly-shell)
    docker exec -it firmaec-wildfly bash
    ;;
  signature-api-start)
    compose up -d signature-api
    ;;
  signature-api-stop)
    compose stop signature-api
    ;;
  signature-api-restart)
    compose restart signature-api
    ;;
  signature-api-status)
    docker exec signature-api curl --fail --silent --show-error http://127.0.0.1:8080/health/live && echo
    compose ps signature-api
    ;;
  signature-api-logs)
    compose logs -f --tail=200 signature-api
    ;;
  signature-api-shell)
    docker exec -it signature-api bash
    ;;
  signature-api-build)
    cat <<'EOF'
signature-api NO se puede construir desde este directorio: su build.context
es la raíz del repo completo (para incluir src/), y aquí solo vive
docs/docker-containers/firmaec/ sin src/.

Use en su lugar:
  scripts/build-signature-api.sh [/ruta/a/una/release/con/el/repo/completo]

Si no se indica ruta, usa la release más reciente en
/opt/uta-containers/releases/signature-api-*/.
EOF
    ;;
  pull)
    compose pull firmaec-postgresql
    docker pull quay.io/wildfly/wildfly:40.0.1.Final-jdk17
    ;;
  build)
    compose build --pull firmaec-wildfly
    ;;
  help|-h|--help)
    usage
    ;;
  *)
    echo "ERROR: comando no reconocido: ${command}" >&2
    usage >&2
    exit 2
    ;;
esac
