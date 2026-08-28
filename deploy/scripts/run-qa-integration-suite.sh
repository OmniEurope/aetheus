#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

SOURCE_ROOT="${1:?source root is required}"
RESULT_LABEL="${2:?result label is required}"
WORKSPACE="${WORKSPACE:?WORKSPACE is required}"
RESULTS_DIR="$WORKSPACE/.qa-test-results/$RESULT_LABEL"
TRX="$RESULTS_DIR/integration.trx"

case "$RESULT_LABEL" in *[!a-zA-Z0-9_-]*|'') echo "Invalid integration result label." >&2; exit 2 ;; esac
test -f "$SOURCE_ROOT/tests/Aetheus.Back.IntegrationTests/Aetheus.Back.IntegrationTests.csproj"

DOTNET="$(sh "$WORKSPACE/deploy/scripts/ensure-dotnet-sdk.sh")"
DOTNET_ROOT="$(dirname "$DOTNET")"
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"
NODE="$(sh "$WORKSPACE/deploy/scripts/ensure-node-runtime.sh")"

# The prebuilt runtime manifest contains absolute content roots from its CI build
# workspace. Rebase them to the restored V or V-1 source before WebApplicationFactory
# initializes its physical file providers.
"$NODE" "$WORKSPACE/deploy/scripts/rebase-static-web-assets.mjs" "$SOURCE_ROOT"

rm -rf "$RESULTS_DIR"
mkdir -p "$RESULTS_DIR"
cd "$SOURCE_ROOT"
RAW_TEST_EXIT=0
sh "$WORKSPACE/deploy/scripts/run-with-progress.sh" "Integration tests ($RESULT_LABEL)" \
  "$DOTNET" test tests/Aetheus.Back.IntegrationTests --configuration Release --no-build --no-restore \
  --logger "console;verbosity=minimal" --logger "trx;LogFileName=integration.trx" \
  --results-directory "$RESULTS_DIR" || RAW_TEST_EXIT=$?

STATUS="$("$NODE" "$WORKSPACE/deploy/scripts/classify-dotnet-test-result.mjs" "$RAW_TEST_EXIT" "$TRX" -)"
echo "Integration suite $RESULT_LABEL produced an executed-test proof with findings status $STATUS."
[ "$STATUS" = 0 ] || exit 10
