#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# The vitrine side of a production cutover, as its own two-phase transaction.
#
# `bluegreen-rollback` restores the application colour and the upstream configuration it snapshotted.
# It knows nothing about the static site published on the same host, so a rolled-back deployment would
# have left the new vitrine serving next to the previous application version. This snapshots the web
# root before publishing and restores it on the rollback path, which is what keeps the two in step.
#
#   publish  - snapshot the current web root, then publish the candidate's vitrine
#   commit   - drop the snapshot once the deployment is committed and can no longer be rolled back
#   rollback - put the snapshot back; absence of a snapshot means nothing was published, so the root
#              is emptied rather than left holding a version nobody chose
set -eu

fail() {
  echo "FATAL: $*" >&2
  exit 1
}

ACTION="${1:-}"
case "$ACTION" in
  publish|commit|rollback) ;;
  *) fail "Usage: prod-vitrine-transaction.sh publish|commit|rollback [vitrine|docs]" ;;
esac

# Second static property published from the same cutover: the documentation site. It gets its own
# web root, its own snapshot directory and its own source inside the payload, so the two properties
# are published and rolled back independently while sharing this one transaction implementation.
PROPERTY="${2:-vitrine}"
case "$PROPERTY" in
  vitrine)
    WEB_ROOT="${SITE_WEB_ROOT:?SITE_WEB_ROOT is required}"
    DOMAIN_VALUE="${SITE_DOMAIN:-}"
    DOMAIN_NAME="SITE_DOMAIN"
    SOURCE_SUBDIR="site"
    SNAPSHOT_NAME="vitrine-transaction"
    ;;
  docs)
    WEB_ROOT="${DOCS_WEB_ROOT:?DOCS_WEB_ROOT is required}"
    DOMAIN_VALUE="${DOCS_DOMAIN:-}"
    DOMAIN_NAME="DOCS_DOMAIN"
    SOURCE_SUBDIR="docs-site/public"
    SNAPSHOT_NAME="docs-transaction"
    ;;
  *) fail "Unknown property: $PROPERTY" ;;
esac

# The prefix alone is not a boundary: /var/www/aetheus-x/../../../etc matches it and would then be
# handed to `find ... -exec rm -rf`. Refuse traversal, and refuse any deeper path, so the value can
# only ever be one directory directly under /var/www. Enforced for every property.
case "$WEB_ROOT" in
  *..*) fail "Unsafe $PROPERTY web root (path traversal): $WEB_ROOT" ;;
  /var/www/*/*) fail "Unsafe $PROPERTY web root (must be a direct child of /var/www): $WEB_ROOT" ;;
  /var/www/?*) ;;
  *) fail "Unsafe $PROPERTY web root (must live under /var/www): $WEB_ROOT" ;;
esac
[ ! -L "$WEB_ROOT" ] || fail "The $PROPERTY web root must not be symbolic."

STATE_DIR="$(dirname "${ENV_FILE:?ENV_FILE is required}")"
# Same shape check as prod-deploy-prepare.sh: a real, direct child of /var/lib. The `aetheus-`
# prefix that used to be required here and on the web root above named the project rather than
# bounding the path, and it is the library's job to name things.
case "$STATE_DIR" in
  *..*) fail "Unsafe production state directory (path traversal): $STATE_DIR" ;;
  /var/lib/*/*) fail "Unsafe production state directory (must be a direct child of /var/lib): $STATE_DIR" ;;
  /var/lib/?*) ;;
  *) fail "Unsafe production state directory (must live under /var/lib): $STATE_DIR" ;;
esac
SNAPSHOT_DIR="$STATE_DIR/$SNAPSHOT_NAME"

if [ "$ACTION" = commit ]; then
  # Idempotent and never fatal: the deployment is already committed, so failing to remove a snapshot
  # must not turn a successful release into a failed stage.
  rm -rf "$SNAPSHOT_DIR" 2>/dev/null || true
  echo ">>> ${PROPERTY} transaction closed."
  exit 0
fi

if [ "$ACTION" = rollback ]; then
  if [ ! -d "$SNAPSHOT_DIR" ]; then
    echo ">>> No ${PROPERTY} transaction is open; nothing to restore."
    exit 0
  fi
  if [ -f "$SNAPSHOT_DIR/was-absent" ]; then
    [ ! -d "$WEB_ROOT" ] || find "$WEB_ROOT" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
    echo ">>> Restored the ${PROPERTY} to absent, as it was before this deployment."
  elif [ -d "$SNAPSHOT_DIR/before" ]; then
    mkdir -p "$WEB_ROOT"
    find "$WEB_ROOT" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
    cp -a "$SNAPSHOT_DIR/before"/. "$WEB_ROOT"/
    echo ">>> Restored the ${PROPERTY} that was serving before this deployment."
  else
    fail "The ${PROPERTY} transaction is open but holds neither a snapshot nor an absence marker."
  fi
  rm -rf "$SNAPSHOT_DIR"
  exit 0
fi

WORKSPACE="${WORKSPACE:?WORKSPACE is required}"
DOMAIN="$DOMAIN_VALUE"
case "$DOMAIN" in ""|*"$DOMAIN_NAME"*) fail "$DOMAIN_NAME was not provided by the Variable Library." ;; esac
VITRINE_SOURCE="$WORKSPACE/.delivery-vitrine/$SOURCE_SUBDIR"
[ -s "$VITRINE_SOURCE/index.html" ] || fail "The staged ${PROPERTY} is missing; the preparation stage did not run."

rm -rf "$SNAPSHOT_DIR"
install -d -m 700 "$SNAPSHOT_DIR"
if [ -d "$WEB_ROOT" ]; then
  mkdir "$SNAPSHOT_DIR/before"
  cp -a "$WEB_ROOT"/. "$SNAPSHOT_DIR/before"/
else
  : > "$SNAPSHOT_DIR/was-absent"
fi

sh "$WORKSPACE/deploy/scripts/publish-vitrine.sh" "$DOMAIN" "$WEB_ROOT" "$VITRINE_SOURCE"
echo "The ${PROPERTY} was published for $DOMAIN; the previous one is retained until the deployment commits."
