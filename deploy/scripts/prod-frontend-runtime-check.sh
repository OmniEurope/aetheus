#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# The frontend runtime contract the generic smoke step cannot express.
#
# `type: smoke` verifies that the shell is real HTML and that it is not cacheable. Both matter, and
# both are generic. This one checks that the OmniEurope assets the interface is built on are
# actually served. A mismatch can leave every health probe green while the interface fails in the
# browser.
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

OMNI_CSS_PATH="$(printf '%s\n' "$SHELL_HTML" \
  | sed -n 's/.*href="\([^"]*_content\/OmniEurope\.Blazor\/omnieurope\.blazor\.css\)".*/\1/p' | head -n 1)"
[ -n "$OMNI_CSS_PATH" ] || fail "The frontend does not reference the OmniEurope.Blazor stylesheet."

OMNI_SMOKE_FILE="$(mktemp)"
trap 'rm -f "$OMNI_SMOKE_FILE"' EXIT
curl -fsS --connect-timeout 5 --max-time 30 "$ORIGIN/$OMNI_CSS_PATH" > "$OMNI_SMOKE_FILE" \
  || fail "The OmniEurope.Blazor stylesheet is not reachable."
grep -q '\.omni-' "$OMNI_SMOKE_FILE" \
  || fail "The served OmniEurope.Blazor stylesheet is incompatible with the published WASM."
curl -fsS --connect-timeout 5 --max-time 30 "$ORIGIN/_content/OmniEurope.Blazor/omniInterop.js" > "$OMNI_SMOKE_FILE" \
  || fail "The OmniEurope.Blazor JavaScript module is not reachable."
grep -q 'focusFirstInvalid' "$OMNI_SMOKE_FILE" \
  || fail "The served OmniEurope.Blazor JavaScript module is incompatible with the published WASM."

echo "Frontend runtime contract passed: the OmniEurope assets match the published WASM."
