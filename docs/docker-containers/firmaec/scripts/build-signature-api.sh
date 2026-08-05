#!/usr/bin/env bash
# Construye la imagen signature-api y la despliega en /opt/uta-containers/electronic-signature.
#
# OJO — restricción no obvia: en compose.yaml, signature-api tiene
# `build.context: ../../..` (la raíz del repo completo, para poder incluir `src/`).
# Eso SOLO resuelve bien si "docker compose build" se ejecuta desde una copia
# completa del repo (con src/ y docs/ presentes), NUNCA desde
# /opt/uta-containers/electronic-signature (que solo tiene la carpeta
# docs/docker-containers/firmaec/, sin src/) — ahí el contexto resuelve a "/" y
# el build falla. Por eso este script exige un RELEASE_DIR con el repo completo,
# en vez de reusar manage-firmaec.sh/install-firmaec.sh (que sí operan desde el
# directorio de instalación "pelado").
set -Eeuo pipefail

RELEASES_ROOT="${UTA_RELEASES_DIR:-/opt/uta-containers/releases}"
INSTALL_DIR="${UTA_CONTAINERS_DIR:-/opt/uta-containers/electronic-signature}"

fail() { echo "ERROR: $*" >&2; exit 1; }

# RELEASE_DIR: ruta a una copia completa del repo (con src/ y docs/). Si no se
# indica, se usa la release con el timestamp más reciente en RELEASES_ROOT.
release_dir="${1:-}"
if [[ -z "${release_dir}" ]]; then
  release_dir="$(find "${RELEASES_ROOT}" -mindepth 1 -maxdepth 1 -type d -name 'signature-api-*' | sort | tail -n1)"
  [[ -n "${release_dir}" ]] || fail "no hay ninguna release en ${RELEASES_ROOT}. Indique la ruta como argumento."
fi

[[ -d "${release_dir}/src" ]] || fail "'${release_dir}' no parece una copia completa del repo (falta src/)."
compose_dir="${release_dir}/docs/docker-containers/firmaec"
[[ -f "${compose_dir}/compose.yaml" ]] || fail "no se encontró compose.yaml en ${compose_dir}."

echo "Release usada: ${release_dir}"
echo "Construyendo signature-api..."
(cd "${compose_dir}" && docker compose build signature-api)

echo "Recreando el contenedor con la imagen nueva..."
(cd "${INSTALL_DIR}" && docker compose --env-file .env -f compose.yaml up -d --force-recreate signature-api)

echo
echo "Listo. Para desplegar solo un cambio de código (sin nueva release completa):"
echo "  1. Copie los archivos .cs cambiados a '${release_dir}/src/...' (mismo path relativo)."
echo "  2. Vuelva a ejecutar este script con la misma release: $0 '${release_dir}'"
