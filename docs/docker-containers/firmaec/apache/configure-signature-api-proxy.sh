#!/usr/bin/env bash
set -Eeuo pipefail

APACHE_SITE="${SIGNATURE_APACHE_SITE:-/etc/apache2/sites-available/uta-proxy.conf}"
BACKUP_DIR="${SIGNATURE_APACHE_BACKUP_DIR:-/var/backups/uta-containers/apache}"
BEGIN_MARKER="# BEGIN UTA SIGNATURE API"
END_MARKER="# END UTA SIGNATURE API"

require_root() {
  [[ "${EUID}" -eq 0 ]] || {
    echo "ERROR: ejecute con sudo." >&2
    exit 1
  }
}

backup_site() {
  local timestamp backup
  timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
  mkdir -p "${BACKUP_DIR}"
  backup="${BACKUP_DIR}/uta-proxy.signature-api.${timestamp}.bak"
  cp --preserve=mode,ownership,timestamps "${APACHE_SITE}" "${backup}"
  echo "Respaldo: ${backup}"
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

validate_and_reload() {
  apachectl configtest
  systemctl reload apache2
  systemctl is-active --quiet apache2
}

install_proxy() {
  local temporary
  require_root
  [[ -f "${APACHE_SITE}" ]] || {
    echo "ERROR: no existe ${APACHE_SITE}." >&2
    exit 1
  }
  curl --fail --silent --show-error \
    http://127.0.0.1:5060/health/live >/dev/null

  a2enmod proxy proxy_http proxy_wstunnel headers rewrite >/dev/null
  backup_site
  remove_block
  temporary="$(mktemp)"
  trap 'rm -f "${temporary:-}"' EXIT

  awk -v begin="${BEGIN_MARKER}" -v end="${END_MARKER}" '
    /^[[:space:]]*ProxyPass[[:space:]]+"\/"[[:space:]]+/ && inserted == 0 {
      print "    " begin
      print "    RedirectMatch 302 ^/signature-api$ /signature-api/"
      print "    ProxyPass        \"/signature-api/signatureHub\" \"ws://127.0.0.1:5060/signatureHub\" retry=0 connectiontimeout=5 timeout=120"
      print "    ProxyPassReverse \"/signature-api/signatureHub\" \"ws://127.0.0.1:5060/signatureHub\""
      print "    ProxyPass        \"/signature-api/\" \"http://127.0.0.1:5060/\" retry=0 connectiontimeout=5 timeout=300 nocanon"
      print "    ProxyPassReverse \"/signature-api/\" \"http://127.0.0.1:5060/\""
      print "    <Location \"/signature-api/\">"
      print "        RequestHeader set X-Forwarded-Proto \"https\""
      print "        RequestHeader set X-Forwarded-Port \"443\""
      print "        LimitRequestBody 23068672"
      print "    </Location>"
      print "    " end
      print ""
      inserted = 1
    }
    { print }
    END { if (inserted == 0) exit 42 }
  ' "${APACHE_SITE}" > "${temporary}"

  install -o root -g root -m 0644 "${temporary}" "${APACHE_SITE}"
  validate_and_reload
  echo "URL API: https://portal.uta.edu.ec/signature-api/"
}

remove_proxy() {
  require_root
  if grep -Fq "${BEGIN_MARKER}" "${APACHE_SITE}"; then
    backup_site
    remove_block
    validate_and_reload
  fi
  echo "Proxy signature-api retirado."
}

status_proxy() {
  if grep -Fq "${BEGIN_MARKER}" "${APACHE_SITE}"; then
    echo "Bloque Apache: presente"
  else
    echo "Bloque Apache: ausente"
  fi
  curl -skS -o /dev/null \
    -w 'HTTPS health HTTP=%{http_code}\n' \
    https://portal.uta.edu.ec/signature-api/health/live
}

case "${1:-status}" in
  install) install_proxy ;;
  remove) remove_proxy ;;
  status) status_proxy ;;
  *)
    echo "Uso: $0 {install|remove|status}" >&2
    exit 2
    ;;
esac
