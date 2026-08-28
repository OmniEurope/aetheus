#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

SCRIPT="$(dirname "$0")/deploy-exit-action.sh"
test "$(sh "$SCRIPT" 0 no no)" = none
test "$(sh "$SCRIPT" 1 no no)" = cleanup
test "$(sh "$SCRIPT" 1 yes no)" = rollback
test "$(sh "$SCRIPT" 1 yes yes)" = none
if sh "$SCRIPT" missing yes no >/dev/null 2>&1; then
  echo "invalid state unexpectedly accepted" >&2
  exit 1
fi
printf '%s\n' "Deploy exit decision harness passed."
