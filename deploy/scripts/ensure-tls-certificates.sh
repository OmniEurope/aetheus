#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
#
# The "TLS" step of a deployment (PLAN-003 2.1): every host the environment serves has a certificate
# that is valid for at least TLS_MIN_DAYS more days, or the run fails.
#
# Before this, the production certificate (aetheus, app, api) had been issued by hand and nothing
# renewed it; only the docs and the demo re-checked theirs. For each argument this script:
#   1. issues the certificate when its lineage does not exist or does not name every requested host;
#   2. renews it when it expires within TLS_MIN_DAYS days, issuing it again with the same names
#      through the web-root helper when `certbot renew` fails;
#   3. then checks the result, and fails the run when a certificate is still missing, still expiring
#      within TLS_MIN_DAYS days, or does not name every host - a renewal that did not happen must not
#      read as one that did.
# Every host is attempted before the verdict, so one failed issuance does not hide the state of the
# others, and each lineage is its own: a failure on one never re-issues another.
#
# Usage: ensure-tls-certificates.sh NAME[,ALIAS...] [NAME[,ALIAS...] ...]
#   NAME is the lineage (the /etc/letsencrypt/live/NAME directory); the aliases are extra names the
#   same certificate must carry (the previous API name, while it is kept).
# Environment:
#   AETHEUS_CERTBOT_EMAIL  Let's Encrypt contact (optional)
#   AGENT_HELPERS_DIR      where the root-owned certbot helpers live (see deploy-identity.sh)
#   TLS_LIVE_DIR           default /etc/letsencrypt/live; readable by the agent (installer ACL)
#   TLS_MIN_DAYS           default 30
set -eu

fail() { echo "FATAL: $*" >&2; exit 1; }

[ "$#" -gt 0 ] || fail "Usage: ensure-tls-certificates.sh NAME[,ALIAS...] ..."
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=deploy-identity.sh
. "$SCRIPT_DIR/deploy-identity.sh"
LIVE_DIR="${TLS_LIVE_DIR:-/etc/letsencrypt/live}"
MIN_DAYS="${TLS_MIN_DAYS:-30}"
case "$MIN_DAYS" in ''|*[!0-9]*) fail "TLS_MIN_DAYS must be a number of days: $MIN_DAYS" ;; esac

valid_host() {
  case "$1" in
    ''|*[!a-zA-Z0-9.-]*|.*|-*|*.|*..*) return 1 ;;
    *.*) return 0 ;;
    *) return 1 ;;
  esac
}

# Days before the certificate expires, or nothing when it cannot be read.
days_left() {
  _end="$(openssl x509 -enddate -noout -in "$1" 2>/dev/null | sed 's/^notAfter=//')" || return 0
  [ -n "$_end" ] || return 0
  _epoch="$(date -d "$_end" +%s 2>/dev/null)" || return 0
  echo $(( (_epoch - $(date +%s)) / 86400 ))
}

# The DNS names the certificate carries, one per line.
dns_names() {
  openssl x509 -noout -ext subjectAltName -in "$1" 2>/dev/null | tr ',' '\n' \
    | sed -n 's/^[[:space:]]*DNS://p'
}

# The same names as the comma list the issue helper takes.
sans_of() {
  dns_names "$1" | paste -sd, -
}

# Whether the certificate names every host of the group.
names_all() {
  _sans="$(dns_names "$1")"
  shift
  for _name in "$@"; do
    printf '%s\n' "$_sans" | grep -qxF "$_name" || return 1
  done
  return 0
}

ISSUE="$AGENT_HELPERS_DIR/aetheus-certbot-issue"
MANAGE="$AGENT_HELPERS_DIR/aetheus-certbot-manage"

for group in "$@"; do
  # An alias that is empty on purpose (the previous API name, once retired) leaves a trailing comma.
  group="${group%,}"
  lineage="${group%%,*}"
  names="$(printf '%s' "$group" | tr ',' ' ')"
  # shellcheck disable=SC2086
  for name in $names; do valid_host "$name" || fail "Not a fully qualified hostname: '$name' in '$group'"; done
  cert="$LIVE_DIR/$lineage/fullchain.pem"

  # shellcheck disable=SC2086
  if [ ! -f "$cert" ] || ! names_all "$cert" $names; then
    echo ">>> Issuing the certificate for $group"
    AETHEUS_CERTBOT_DOMAINS="$group" \
      AETHEUS_CERTBOT_EMAIL="${AETHEUS_CERTBOT_EMAIL:-}" \
      AETHEUS_CERTBOT_MODE="production" \
      sudo -n "$ISSUE" "$lineage" \
      || echo "WARNING: issuance failed for $group; the check below decides." >&2
    continue
  fi

  left="$(days_left "$cert")"
  if [ -z "$left" ] || [ "$left" -lt "$MIN_DAYS" ]; then
    echo ">>> The certificate for $lineage expires in ${left:-an unknown number of} days; renewing"
    if ! sudo -n "$MANAGE" renew "$lineage"; then
      # `certbot renew` replays the authenticator the lineage was first issued with. The main
      # production certificate was issued by hand through the apache plugin, and that plugin wedged
      # on the host ("Unable to revert temporary config", deploy-prod 2367), failing every renewal.
      # Issued again through the web-root helper with exactly the names it already carries, certbot
      # renews that same lineage in place and records the web root for the next renewal.
      # Never with fewer names: a narrower request would open a second lineage and let the hosts it
      # drops expire with this one.
      current="$(sans_of "$cert")"
      if [ -z "$current" ]; then
        echo "WARNING: renewal failed for $lineage and its names are unreadable; the check below decides." >&2
      else
        echo ">>> Renewal failed for $lineage; issuing it again through the web root for: $current"
        AETHEUS_CERTBOT_DOMAINS="$current" \
          AETHEUS_CERTBOT_EMAIL="${AETHEUS_CERTBOT_EMAIL:-}" \
          AETHEUS_CERTBOT_MODE="production" \
          sudo -n "$ISSUE" "$lineage" \
          || echo "WARNING: renewal failed for $lineage; the check below decides." >&2
      fi
    fi
  else
    echo ">>> $lineage: valid for $left more days"
  fi
done

FAILED=""
for group in "$@"; do
  group="${group%,}"
  lineage="${group%%,*}"
  names="$(printf '%s' "$group" | tr ',' ' ')"
  cert="$LIVE_DIR/$lineage/fullchain.pem"
  if [ ! -f "$cert" ]; then
    FAILED="$FAILED
  $lineage: no certificate at $cert (or the agent cannot read $LIVE_DIR)"
    continue
  fi
  # shellcheck disable=SC2086
  names_all "$cert" $names || FAILED="$FAILED
  $lineage: the certificate does not name every host of '$group'"
  left="$(days_left "$cert")"
  if [ -z "$left" ] || [ "$left" -lt "$MIN_DAYS" ]; then
    FAILED="$FAILED
  $lineage: expires in ${left:-an unreadable number of} days, fewer than $MIN_DAYS"
  fi
done

[ -z "$FAILED" ] || fail "TLS is not in order after issuance and renewal:$FAILED"
echo ">>> Every certificate is valid for at least $MIN_DAYS more days: $*"
