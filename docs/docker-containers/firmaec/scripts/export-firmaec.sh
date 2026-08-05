#!/usr/bin/env bash
set -Eeuo pipefail

INSTALL_DIR="${UTA_CONTAINERS_DIR:-/opt/uta-containers/electronic-signature}"
EXPORT_ROOT="${1:-${INSTALL_DIR}/exports}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
bundle_dir="${EXPORT_ROOT}/firmaec-${timestamp}"
archive="${EXPORT_ROOT}/firmaec-${timestamp}.tar.gz"

cd "${INSTALL_DIR}"
mkdir -p "${bundle_dir}"

wildfly_image="$(awk -F= '$1=="FIRMAEC_IMAGE"{print $2}' .env)"
postgres_image="$(awk -F= '$1=="POSTGRES_IMAGE"{print $2}' .env)"
signature_api_image="$(awk -F= '$1=="SIGNATURE_API_IMAGE"{print $2}' .env)"

echo "Generando respaldo lógico PostgreSQL..."
docker exec firmaec-postgresql \
  pg_dump -U firmadigital -d firmadigital -Fc \
  > "${bundle_dir}/firmadigital.dump"

echo "Exportando imágenes Docker (WildFly, PostgreSQL y signature-api)..."
# signature-api ya debe existir localmente (docker images) antes de correr esto —
# este script solo empaqueta imágenes ya construidas, no las construye. Si falta,
# constrúyala primero con scripts/build-signature-api.sh.
docker image save "${wildfly_image}" "${postgres_image}" "${signature_api_image}" \
  | gzip -9 > "${bundle_dir}/docker-images.tar.gz"

echo "Empaquetando configuración sin secretos..."
tar \
  --exclude='./secrets' \
  --exclude='./exports' \
  --exclude='./.env' \
  -czf "${bundle_dir}/configuration.tar.gz" \
  compose.yaml .env.example README.md artifacts wildfly scripts

cp .env.example "${bundle_dir}/destination.env.example"

cat > "${bundle_dir}/SECRETS-NOT-INCLUDED.txt" <<'EOF'
Los secretos no están incluidos deliberadamente.
Transfiéralos por un canal institucional seguro y colóquelos en:
  secrets/postgres_password
  secrets/wildfly_management_password
  secrets/firmaec_client_api_key
  secrets/firmaec_callback_api_key
  secrets/signature_database_connection      (cadena de conexión SQL Server real)
  secrets/repositoryuta_service_client_secret (secreto real del cliente en RepositoryUta)
con permisos 0640 root:docker. Los dos últimos son de signature-api y NO se
pueden generar al azar — deben ser los valores reales del ambiente destino.
EOF

(
  cd "${bundle_dir}"
  sha256sum \
    firmadigital.dump \
    docker-images.tar.gz \
    configuration.tar.gz \
    > SHA256SUMS
)

tar -C "${EXPORT_ROOT}" -czf "${archive}" "$(basename "${bundle_dir}")"
sha256sum "${archive}" > "${archive}.sha256"

echo "Paquete creado: ${archive}"
echo "Copiar a otro servidor:"
echo "  scp '${archive}' '${archive}.sha256' usuario@SERVIDOR:/tmp/"

