#!/usr/bin/env bash
set -Eeuo pipefail

if [[ $# -ne 1 ]]; then
  echo "Uso: import-firmaec.sh /ruta/firmaec-FECHA.tar.gz" >&2
  exit 2
fi

archive="$(readlink -f "$1")"
work_dir="$(mktemp -d)"
trap 'rm -rf "${work_dir}"' EXIT

if [[ -f "${archive}.sha256" ]]; then
  (cd "$(dirname "${archive}")" && sha256sum -c "$(basename "${archive}.sha256")")
fi

tar -xzf "${archive}" -C "${work_dir}"
bundle_dir="$(find "${work_dir}" -mindepth 1 -maxdepth 1 -type d -name 'firmaec-*' | head -n 1)"

if [[ -z "${bundle_dir}" ]]; then
  echo "ERROR: estructura de paquete inválida." >&2
  exit 1
fi

(cd "${bundle_dir}" && sha256sum -c SHA256SUMS)

echo "Cargando imágenes Docker..."
gzip -dc "${bundle_dir}/docker-images.tar.gz" | docker image load

echo
echo "Las imágenes quedaron cargadas."
echo "Antes de levantar servicios:"
echo "  1. Extraiga configuration.tar.gz en /opt/uta-containers/electronic-signature."
echo "  2. Cree .env desde destination.env.example."
echo "  3. Transfiera los secretos mediante un canal seguro."
echo "  4. Ejecute scripts/install-firmaec.sh install."
echo "  5. Restaure firmadigital.dump solo en una base vacía y validada."
echo
echo "No se restauró ni sobrescribió ninguna base automáticamente."

