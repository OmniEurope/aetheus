# SPDX-License-Identifier: EUPL-1.2
# SPDX-FileCopyrightText: 2026 Aetheus contributors
#
# Provisions the documentation site's HTTPS vhost and its web root, once.
#
# The docs templates (.pipeline/configs/apache/aetheus-docs-*.conf) existed for weeks with nothing
# rendering them: the deployment published the docs FILES into the docs web root and stopped there,
# so the site was on disk and served by nobody. This closes that gap: render the versioned template,
# enable it, reload Apache through the root-owned reload helper, so a fresh host needs no manual
# Apache work at all.
#
# PLAN-003 2.1: the certificate is no longer this script's. The deployment's TLS stage
# (ensure-tls-certificates.sh) issues, renews and checks every host it serves, DOCS_DOMAIN included,
# and the HTTP vhost is applied by the stage before it. What is left here is the HTTPS vhost, which
# needs the web root to exist (Apache refuses a DocumentRoot that is not a directory).
#
# Idempotent by design: it does nothing when the vhost already exists, because it is live state the
# operator may have adjusted.
set -eu

fail() { echo "FATAL: $*" >&2; exit 1; }

DOCS_DOMAIN="${DOCS_DOMAIN:?DOCS_DOMAIN is required}"
DOCS_WEB_ROOT="${DOCS_WEB_ROOT:?DOCS_WEB_ROOT is required}"
WORKSPACE="${WORKSPACE:?WORKSPACE is required}"
# The Apache directories are the ones the environment's upstream file lives in (aetheus.prod), so
# this script restates no path of the host.
SITES_AVAILABLE="$(dirname "${UPSTREAM_CONF:?UPSTREAM_CONF is required}")"
SITES_ENABLED="$(dirname "${UPSTREAM_LINK:?UPSTREAM_LINK is required}")"
# shellcheck source=deploy-identity.sh
. "$WORKSPACE/deploy/scripts/deploy-identity.sh"

# Same boundary the vitrine transaction enforces: the value can only ever name one directory
# directly under /var/www, so a crafted variable cannot point Apache at an arbitrary path.
case "$DOCS_WEB_ROOT" in
  *..*) fail "Unsafe docs web root (path traversal): $DOCS_WEB_ROOT" ;;
  /var/www/*/*) fail "Unsafe docs web root (must be a direct child of /var/www): $DOCS_WEB_ROOT" ;;
  /var/www/?*) ;;
  *) fail "Unsafe docs web root (must live under /var/www): $DOCS_WEB_ROOT" ;;
esac

# A hostname, not a shell expression: this value is interpolated into a root-owned Apache
# configuration, so it is validated before it is.
case "$DOCS_DOMAIN" in
  *[!a-zA-Z0-9.-]*|-*|.*|*.) fail "Unsafe docs domain: $DOCS_DOMAIN" ;;
esac

HTTPS_TEMPLATE="$WORKSPACE/.pipeline/configs/apache/aetheus-docs-https.conf"
[ -f "$HTTPS_TEMPLATE" ] || fail "Versioned docs HTTPS vhost template is missing."
HTTPS_CONF="$SITES_AVAILABLE/$DOCS_DOMAIN-ssl.conf"

# --- 0. Web root ----------------------------------------------------------------------------------
# Before the vhost, not after: Apache answers 403 to every request on a vhost whose DocumentRoot
# does not exist. Publishing the files later still empties and repopulates it.
mkdir -p "$DOCS_WEB_ROOT"

# --- 1. The HTTPS vhost ---------------------------------------------------------------------------
if [ -e "$HTTPS_CONF" ]; then
  echo ">>> Documentation HTTPS vhost already present; nothing to change."
  exit 0
fi
[ ! -L "$HTTPS_CONF" ] || fail "The docs vhost must not be symbolic: $HTTPS_CONF"
RENDER_TMP="$HTTPS_CONF.render.$$"
sed \
  -e "s|#{DOCS_DOMAIN}#|$DOCS_DOMAIN|g" \
  -e "s|#{DOCS_WEB_ROOT}#|$DOCS_WEB_ROOT|g" \
  "$HTTPS_TEMPLATE" > "$RENDER_TMP"
# A leftover token would be served verbatim by Apache, or refuse the reload. Neither is silent, but
# both are worse than refusing here with the file still unwritten.
if grep -q '#{' "$RENDER_TMP"; then
  rm -f "$RENDER_TMP"
  fail "The docs Apache template contains an unresolved token."
fi
mv "$RENDER_TMP" "$HTTPS_CONF"
ln -sf "$HTTPS_CONF" "$SITES_ENABLED/$(basename "$HTTPS_CONF")"
echo ">>> Rendered and enabled $(basename "$HTTPS_CONF")"
sudo -n "$AGENT_HELPERS_DIR/aetheus-apache-reload" \
  || fail "Apache rejected the documentation vhost."
echo ">>> Apache reloaded; $DOCS_DOMAIN is served from $DOCS_WEB_ROOT."
