#!/usr/bin/env bash
set -Eeuo pipefail

APACHE_SITE="${FIRMAEC_APACHE_SITE:-/etc/apache2/sites-available/uta-proxy.conf}"
BACKUP_DIR="${FIRMAEC_APACHE_BACKUP_DIR:-/var/backups/uta-containers/apache}"
STATE_DIR="/var/lib/uta-containers/wildfly-console"
STATE_FILE="${STATE_DIR}/temporary-access.env"
BEGIN_MARKER="# BEGIN UTA WILDFLY CONSOLE TEMPORARY"
END_MARKER="# END UTA WILDFLY CONSOLE TEMPORARY"

usage() {
  cat <<'EOF'
Uso: configure-wildfly-console.sh COMANDO

  enable IP [MINUTOS]  Habilita acceso HTTPS para una IP; 120 minutos por defecto.
  status               Muestra IP, vencimiento y respuesta HTTPS.
  disable              Retira inmediatamente la publicación temporal.
EOF
}

require_root() {
  [[ "${EUID}" -eq 0 ]] || {
    echo "ERROR: ejecute con sudo." >&2
    exit 1
  }
}

backup_site() {
  local backup timestamp
  timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
  mkdir -p "${BACKUP_DIR}"
  backup="${BACKUP_DIR}/uta-proxy.console.${timestamp}.bak"
  cp --preserve=mode,ownership,timestamps "${APACHE_SITE}" "${backup}"
  echo "Respaldo: ${backup}"
}

validate_and_reload() {
  apachectl configtest
  systemctl reload apache2
  systemctl is-active --quiet apache2
}

remove_block() {
  local temporary
  grep -Fq "${BEGIN_MARKER}" "${APACHE_SITE}" || return 0
  temporary="$(mktemp)"
  awk -v begin="${BEGIN_MARKER}" -v end="${END_MARKER}" '
    index($0, begin) { skipping = 1; next }
    index($0, end) { skipping = 0; next }
    !skipping { print }
  ' "${APACHE_SITE}" > "${temporary}"
  install -o root -g root -m 0644 "${temporary}" "${APACHE_SITE}"
  rm -f "${temporary}"
}

enable_console() {
  local allowed_ip="${1:-}" duration="${2:-120}"
  local expiry_apache expiry_epoch expiry_text temporary unit_name
  require_root

  [[ "${allowed_ip}" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || {
    echo "ERROR: indique una dirección IPv4 administrativa." >&2
    exit 2
  }
  [[ "${duration}" =~ ^[0-9]+$ ]] \
    && (( duration >= 15 && duration <= 480 )) || {
      echo "ERROR: la duración debe estar entre 15 y 480 minutos." >&2
      exit 2
    }
  [[ -f "${APACHE_SITE}" ]] || {
    echo "ERROR: no existe ${APACHE_SITE}." >&2
    exit 1
  }
  management_code="$(
    curl -sS -o /dev/null -w '%{http_code}' \
      http://127.0.0.1:9990/management
  )"
  [[ "${management_code}" == "200" || "${management_code}" == "401" ]] || {
      echo "ERROR: WildFly Management no responde en loopback:9990." >&2
      exit 1
    }

  expiry_epoch="$(date -d "+${duration} minutes" +%s)"
  expiry_apache="$(date -d "@${expiry_epoch}" +%Y%m%d%H%M%S)"
  expiry_text="$(date -d "@${expiry_epoch}" --iso-8601=seconds)"
  unit_name="uta-wildfly-console-expiry-${expiry_epoch}"

  if [[ -r "${STATE_FILE}" ]]; then
    previous_unit="$(awk -F= '$1=="SYSTEMD_UNIT"{print $2}' "${STATE_FILE}")"
    [[ -z "${previous_unit}" ]] \
      || systemctl stop "${previous_unit}.timer" 2>/dev/null \
      || true
  fi

  backup_site
  remove_block
  temporary="$(mktemp)"
  trap 'rm -f "${temporary:-}"' EXIT

  awk \
    -v begin="${BEGIN_MARKER}" \
    -v end="${END_MARKER}" \
    -v ip="${allowed_ip}" \
    -v expiry="${expiry_apache}" '
    /^[[:space:]]*ProxyPass[[:space:]]+"\/"[[:space:]]+/ && inserted == 0 {
      print "    " begin
      print "    # Acceso restringido por IP y hora; WildFly realiza la autenticación."
      print "    RedirectMatch 302 ^/firmaec-console$ /firmaec-console/"
      print "    ProxyPass        \"/firmaec-console/\" \"http://127.0.0.1:9990/\" retry=0 connectiontimeout=5 timeout=120"
      print "    ProxyPassReverse \"/firmaec-console/\" \"http://127.0.0.1:9990/\""
      print "    ProxyPass        \"/management\" \"http://127.0.0.1:9990/management\" retry=0 connectiontimeout=5 timeout=120 nocanon"
      print "    ProxyPassReverse \"/management\" \"http://127.0.0.1:9990/management\""
      print "    <LocationMatch \"^/(firmaec-console|management)(/|$)\">"
      print "        <RequireAll>"
      print "            Require ip " ip
      print "            Require expr \"%{TIME} < \x27" expiry "\x27\""
      print "        </RequireAll>"
      print "        RequestHeader set X-Forwarded-Proto \"https\""
      print "        RequestHeader set X-Forwarded-Port \"443\""
      print "        Header edit Location \"^/console/\" \"/firmaec-console/console/\""
      print "    </LocationMatch>"
      print "    " end
      print ""
      inserted = 1
    }
    { print }
    END { if (inserted == 0) exit 42 }
  ' "${APACHE_SITE}" > "${temporary}"

  install -o root -g root -m 0644 "${temporary}" "${APACHE_SITE}"
  validate_and_reload

  install -d -o root -g root -m 0755 "${STATE_DIR}"
  {
    echo "ALLOWED_IP=${allowed_ip}"
    echo "EXPIRES_EPOCH=${expiry_epoch}"
    echo "EXPIRES_AT=${expiry_text}"
    echo "SYSTEMD_UNIT=${unit_name}"
  } > "${STATE_FILE}"
  chmod 0644 "${STATE_FILE}"

  systemd-run \
    --quiet \
    --unit="${unit_name}" \
    --on-active="${duration}m" \
    "${0}" disable

  echo "URL: https://portal.uta.edu.ec/firmaec-console/"
  echo "IP permitida: ${allowed_ip}"
  echo "Vence: ${expiry_text}"
}

disable_console() {
  require_root
  if grep -Fq "${BEGIN_MARKER}" "${APACHE_SITE}"; then
    backup_site
    remove_block
    validate_and_reload
  fi
  rm -f "${STATE_FILE}"
  echo "Consola WildFly despublicada."
}

show_status() {
  if [[ -r "${STATE_FILE}" ]]; then
    # El archivo contiene únicamente IP y fechas, no secretos.
    cat "${STATE_FILE}"
  else
    echo "No existe acceso temporal registrado."
  fi
  if grep -Fq "${BEGIN_MARKER}" "${APACHE_SITE}"; then
    echo "Bloque Apache: presente"
  else
    echo "Bloque Apache: ausente"
  fi
  curl -skS -o /dev/null \
    -w 'HTTPS consola HTTP=%{http_code}\n' \
    https://portal.uta.edu.ec/firmaec-console/
}

case "${1:-help}" in
  enable) enable_console "${2:-}" "${3:-120}" ;;
  status) show_status ;;
  disable) disable_console ;;
  help|-h|--help) usage ;;
  *)
    echo "ERROR: comando no reconocido: ${1:-}" >&2
    usage >&2
    exit 2
    ;;
esac
