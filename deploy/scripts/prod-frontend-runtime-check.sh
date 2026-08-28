#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# The frontend runtime contract the generic smoke step cannot express.
#
# `type: smoke` verifies that the shell is real HTML and that it is not cacheable. Both matter, and
# both are generic. This one is not: it checks that the Radzen JavaScript the published WASM expects
# is actually the one being served, and that it is cache-busted. A mismatch renders an application
# that answers every health probe and then fails at the first data grid, so no generic readiness check
# would ever catch it.
#
# It runs after the switch and before the evidence stage, which means a failure here leaves the
# cutover uncommitted and hands control to the rollback - the same window the shell path used, moved
# from "before the flip" to "before the commit".
set -eu

fail() {
  echo "FATAL: $*" >&2
  exit 1
}

ORIGIN="${PUBLIC_APP_URL:?PUBLIC_APP_URL is required}"
ORIGIN="${ORIGIN%/}"

SHELL_HTML="$(curl -fsS --connect-timeout 5 --max-time 30 --retry 5 --retry-delay 2 --retry-connrefused "$ORIGIN/")" \
  || fail "The public frontend did not answer after the switch."
printf '%s\n' "$SHELL_HTML" | grep -qi "<html" || fail "The public frontend is not serving an HTML shell."

# The published shell references Radzen's script with a version query. Without it a browser reuses a
# cached script built for another WASM payload.
RADZEN_SCRIPT_PATH="$(printf '%s\n' "$SHELL_HTML" \
  | sed -n 's/.*src="\([^"]*Radzen\.Blazor\.js?v=[^"]*\)".*/\1/p' | head -n 1)"
[ -n "$RADZEN_SCRIPT_PATH" ] || fail "The frontend does not cache-bust its Radzen JavaScript."

RADZEN_SMOKE_FILE="$(mktemp)"
trap 'rm -f "$RADZEN_SMOKE_FILE"' EXIT
curl -fsS --connect-timeout 5 --max-time 30 "$ORIGIN/$RADZEN_SCRIPT_PATH" > "$RADZEN_SMOKE_FILE" \
  || fail "The cache-busted Radzen JavaScript is not reachable."
# Two symbols the application calls on its first rendered page. Their absence means the served script
# predates the published WASM.
grep -q 'createDataGrid' "$RADZEN_SMOKE_FILE" \
  || fail "The served Radzen JavaScript is incompatible with the published WASM (createDataGrid missing)."
grep -q 'createSplitButton' "$RADZEN_SMOKE_FILE" \
  || fail "The served Radzen JavaScript is incompatible with the published WASM (createSplitButton missing)."

echo "Frontend runtime contract passed: Radzen JavaScript is cache-busted and matches the published WASM."
