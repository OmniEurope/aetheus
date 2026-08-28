# SPDX-License-Identifier: EUPL-1.2
# SPDX-FileCopyrightText: 2026 Aetheus contributors
#
# Provisions the documentation site's Apache vhosts and its TLS certificate, once.
#
# The docs templates (.pipeline/configs/apache/aetheus-docs-*.conf) existed for weeks with nothing
# rendering them: the deployment published the docs FILES into /var/www/aetheus-docs and stopped
# there, so the site was on disk and served by nobody. This closes that gap the same way the nightly
# demo does it - issue the certificate through the root-owned certbot helper, render the versioned
# templates, enable them, reload Apache through the root-owned reload helper - so a fresh host needs
# no manual Apache work at all.
#
# Idempotent by design: it does nothing when the vhosts already exist, because they are live state
# the operator may have adjusted. Only the certificate is re-checked, and only for expiry.
set -eu

fail() { echo "FATAL: $*" >&2; exit 1; }

DOCS_DOMAIN="${DOCS_DOMAIN:?DOCS_DOMAIN is required}"
DOCS_WEB_ROOT="${DOCS_WEB_ROOT:?DOCS_WEB_ROOT is required}"
WORKSPACE="${WORKSPACE:?WORKSPACE is required}"

# Same boundary the vitrine transaction enforces: the value can only ever name one directory
# directly under /var/www, so a crafted variable cannot point Apache at an arbitrary path.
case "$DOCS_WEB_ROOT" in
  /var/www/aetheus-*) ;;
  *) fail "Unsafe docs web root: $DOCS_WEB_ROOT" ;;
esac
case "$DOCS_WEB_ROOT" in
  *..*) fail "Unsafe docs web root (path traversal): $DOCS_WEB_ROOT" ;;
  /var/www/*/*) fail "Unsafe docs web root (must be a direct child of /var/www): $DOCS_WEB_ROOT" ;;
esac

# A hostname, not a shell expression: this value is interpolated into a root-owned Apache
# configuration and passed to the certbot helper, so it is validated before either sees it.
case "$DOCS_DOMAIN" in
  *[!a-zA-Z0-9.-]*|-*|.*|*.) fail "Unsafe docs domain: $DOCS_DOMAIN" ;;
esac

HTTP_TEMPLATE="$WORKSPACE/.pipeline/configs/apache/aetheus-docs-http.conf"
HTTPS_TEMPLATE="$WORKSPACE/.pipeline/configs/apache/aetheus-docs-https.conf"
[ -f "$HTTP_TEMPLATE" ] || fail "Versioned docs HTTP vhost template is missing."
[ -f "$HTTPS_TEMPLATE" ] || fail "Versioned docs HTTPS vhost template is missing."

HTTP_CONF="/etc/apache2/sites-available/$DOCS_DOMAIN.conf"
HTTPS_CONF="/etc/apache2/sites-available/$DOCS_DOMAIN-ssl.conf"
CERT_LIVE_DIR="/etc/letsencrypt/live/$DOCS_DOMAIN"

# --- 0. Web root ----------------------------------------------------------------------------------
# Before certbot, not after. The vhost for this domain points at this directory, and Apache answers
# 403 to EVERY request on a vhost whose DocumentRoot does not exist - including the ACME challenge,
# which made issuance fail with "Invalid response ... 403". Creating it first is what lets the
# challenge be served. Publishing the files later still empties and repopulates it.
mkdir -p "$DOCS_WEB_ROOT"

# --- 1. TLS ---------------------------------------------------------------------------------------
# Its own certificate rather than a name added to the vitrine's: an expansion re-issues the
# certificate three live vhosts depend on, so a docs-only failure would take the application down
# with it. Separate certificates fail separately.
if [ ! -f "$CERT_LIVE_DIR/fullchain.pem" ]; then
  echo ">>> Provisioning TLS certificate for $DOCS_DOMAIN via the certbot helper"
  AETHEUS_CERTBOT_DOMAINS="$DOCS_DOMAIN" \
    AETHEUS_CERTBOT_EMAIL="${AETHEUS_CERTBOT_EMAIL:-}" \
    AETHEUS_CERTBOT_MODE="production" \
    sudo -n /usr/local/lib/aetheus/aetheus-certbot-issue "$DOCS_DOMAIN" \
    || fail "Certbot certificate issuance failed for $DOCS_DOMAIN."
else
  CERT_EXPIRY_EPOCH="$(date -d "$(openssl x509 -enddate -noout -in "$CERT_LIVE_DIR/fullchain.pem" \
    | sed 's/notAfter=//')" +%s 2>/dev/null || echo 0)"
  CERT_DAYS_LEFT=$(( (CERT_EXPIRY_EPOCH - $(date +%s)) / 86400 ))
  if [ "$CERT_DAYS_LEFT" -lt 30 ]; then
    echo ">>> Certificate for $DOCS_DOMAIN expires in $CERT_DAYS_LEFT days; renewing"
    sudo -n /usr/local/lib/aetheus/aetheus-certbot-manage renew "$DOCS_DOMAIN" || true
  fi
fi

# --- 2. The vhosts --------------------------------------------------------------------------------
render() {
  _render_template="$1"
  _render_target="$2"
  [ ! -e "$_render_target" ] || return 0
  [ ! -L "$_render_target" ] || fail "The docs vhost must not be symbolic: $_render_target"
  _render_tmp="$_render_target.render.$$"
  sed \
    -e "s|#{DOCS_DOMAIN}#|$DOCS_DOMAIN|g" \
    -e "s|#{DOCS_WEB_ROOT}#|$DOCS_WEB_ROOT|g" \
    "$_render_template" > "$_render_tmp"
  # A leftover token would be served verbatim by Apache, or refuse the reload. Neither is silent,
  # but both are worse than refusing here with the file still unwritten.
  if grep -q '#{' "$_render_tmp"; then
    rm -f "$_render_tmp"
    fail "The docs Apache template contains an unresolved token."
  fi
  mv "$_render_tmp" "$_render_target"
  ln -sf "$_render_target" "/etc/apache2/sites-enabled/$(basename "$_render_target")"
  echo ">>> Rendered and enabled $(basename "$_render_target")"
  RELOAD_NEEDED=1
}

RELOAD_NEEDED=0
render "$HTTP_TEMPLATE" "$HTTP_CONF"
render "$HTTPS_TEMPLATE" "$HTTPS_CONF"

if [ "$RELOAD_NEEDED" -eq 1 ]; then
  sudo -n /usr/local/lib/aetheus/aetheus-apache-reload \
    || fail "Apache rejected the documentation vhosts."
  echo ">>> Apache reloaded; $DOCS_DOMAIN is served from $DOCS_WEB_ROOT."
else
  echo ">>> Documentation vhosts already present; nothing to change."
fi
