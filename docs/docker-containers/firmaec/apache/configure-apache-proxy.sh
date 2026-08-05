#!/usr/bin/env bash
set -Eeuo pipefail

APACHE_SITE="${FIRMAEC_APACHE_SITE:-/etc/apache2/sites-available/uta-proxy.conf}"
BACKUP_DIR="${FIRMAEC_APACHE_BACKUP_DIR:-/var/backups/uta-containers/apache}"
BEGIN_MARKER="# BEGIN UTA FIRMAEC MANAGED"
END_MARKER="# END UTA FIRMAEC MANAGED"

usage() {
  cat <<'EOF'
Uso: configure-apache-proxy.sh COMANDO

  install   Respalda Apache y publica /firmaec/ hacia 127.0.0.1:8180/api/.
  status    Muestra el bloque administrado y prueba la ruta HTTPS.
  remove    Retira únicamente el bloque administrado, con respaldo previo.

La consola administrativa WildFly 9990, PostgreSQL 5432 y /servicio/
permanecen sin exposición pública.
EOF
}

require_root() {
  if [[ "${EUID}" -ne 0 ]]; then
    echo "ERROR: ejecute este comando con sudo." >&2
    exit 1
  fi
}

check_requirements() {
  local http_code
  [[ -f "${APACHE_SITE}" ]] || {
    echo "ERROR: no existe ${APACHE_SITE}." >&2
    exit 1
  }
  http_code="$(
    curl -sS -o /dev/null -w '%{http_code}' \
      http://127.0.0.1:8180/api/version
  )"
  [[ "${http_code}" == "405" ]] || {
      echo "ERROR: WildFly no responde en 127.0.0.1:8180." >&2
      exit 1
    }
  apachectl -M 2>/dev/null \
    | grep -Eq 'proxy_module|proxy_http_module' \
    || {
      echo "ERROR: los módulos proxy de Apache no están habilitados." >&2
      exit 1
    }
}

backup_site() {
  local timestamp backup
  timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
  mkdir -p "${BACKUP_DIR}"
  backup="${BACKUP_DIR}/uta-proxy.conf.${timestamp}.bak"
  cp --preserve=mode,ownership,timestamps "${APACHE_SITE}" "${backup}"
  echo "Respaldo: ${backup}"
}

validate_and_reload() {
  apachectl configtest
  systemctl reload apache2
  systemctl is-active --quiet apache2
}

install_proxy() {
  local temporary
  require_root
  check_requirements

  if grep -Fq "${BEGIN_MARKER}" "${APACHE_SITE}"; then
    echo "El proxy FirmaEC ya está configurado."
    validate_and_reload
    return
  fi

  grep -Eq '^[[:space:]]*ProxyPass[[:space:]]+"/"[[:space:]]+' "${APACHE_SITE}" \
    || {
      echo "ERROR: no se encontró el ProxyPass catch-all de Apache." >&2
      exit 1
    }

  backup_site
  temporary="$(mktemp)"
  trap 'rm -f "${temporary:-}"' EXIT

  awk '
    BEGIN { inserted = 0 }
    /^[[:space:]]*ProxyPass[[:space:]]+"\/"[[:space:]]+/ && inserted == 0 {
      print "    # BEGIN UTA FIRMAEC MANAGED"
      print "    # API pública FirmaEC; /servicio y administración permanecen privadas."
      print "    RedirectMatch 302 ^/firmaec$ /firmaec/"
      print "    ProxyPass        \"/firmaec/\" \"http://127.0.0.1:8180/api/\" retry=0 connectiontimeout=5 timeout=300 nocanon"
      print "    ProxyPassReverse \"/firmaec/\" \"http://127.0.0.1:8180/api/\""
      print "    <Location \"/firmaec/\">"
      print "        RequestHeader set X-Forwarded-Proto \"https\""
      print "        RequestHeader set X-Forwarded-Port \"443\""
      print "    </Location>"
      print "    # END UTA FIRMAEC MANAGED"
      print ""
      inserted = 1
    }
    { print }
    END {
      if (inserted == 0) {
        exit 42
      }
    }
  ' "${APACHE_SITE}" > "${temporary}"

  install -o root -g root -m 0644 "${temporary}" "${APACHE_SITE}"
  validate_and_reload
  echo "Proxy publicado en https://portal.uta.edu.ec/firmaec/"
}

remove_proxy() {
  local temporary
  require_root
  [[ -f "${APACHE_SITE}" ]] || {
    echo "ERROR: no existe ${APACHE_SITE}." >&2
    exit 1
  }

  if ! grep -Fq "${BEGIN_MARKER}" "${APACHE_SITE}"; then
    echo "El bloque administrado FirmaEC no está presente."
    return
  fi

  backup_site
  temporary="$(mktemp)"
  trap 'rm -f "${temporary:-}"' EXIT
  awk -v begin="${BEGIN_MARKER}" -v end="${END_MARKER}" '
    index($0, begin) { skipping = 1; next }
    index($0, end) { skipping = 0; next }
    !skipping { print }
  ' "${APACHE_SITE}" > "${temporary}"
  install -o root -g root -m 0644 "${temporary}" "${APACHE_SITE}"
  validate_and_reload
  echo "Proxy FirmaEC retirado."
}

show_status() {
  echo "Apache: $(systemctl is-active apache2)"
  if grep -Fq "${BEGIN_MARKER}" "${APACHE_SITE}"; then
    sed -n "/${BEGIN_MARKER}/,/${END_MARKER}/p" "${APACHE_SITE}"
  else
    echo "Proxy FirmaEC: no configurado"
  fi
  echo
  curl -skS -X POST \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data 'base64=e30=' \
    -o /dev/null \
    -w 'https://portal.uta.edu.ec/firmaec/version HTTP=%{http_code}\n' \
    https://portal.uta.edu.ec/firmaec/version
}

case "${1:-help}" in
  install) install_proxy ;;
  status) show_status ;;
  remove) remove_proxy ;;
  help|-h|--help) usage ;;
  *)
    echo "ERROR: comando no reconocido: ${1:-}" >&2
    usage >&2
    exit 2
    ;;
esac
