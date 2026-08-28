#!/usr/bin/env sh
# SPDX-License-Identifier: EUPL-1.2
# Retain only the already-built production payload required by the next Candidate.
set -eu

COMMIT="$(git rev-parse HEAD)"
case "$COMMIT" in
  *[!0-9a-fA-F]*|'') echo "Resolved source revision is invalid." >&2; exit 1 ;;
esac
if [ "${BUILD_SOURCEVERSION:-}" != "$COMMIT" ]; then
  echo "Checked-out revision does not match BUILD_SOURCEVERSION." >&2
  exit 1
fi

WORKSPACE="${WORKSPACE:-$(pwd)}"
PACKAGE_DIR="$WORKSPACE/.pipeline-artifacts"
BACK_IMAGE="aetheus-back:$COMMIT"
FRONT_IMAGE="aetheus-front:$COMMIT"
docker image inspect "$BACK_IMAGE" "$FRONT_IMAGE" >/dev/null

rm -rf "$PACKAGE_DIR"
mkdir -p "$PACKAGE_DIR"
printf '%s\n' "$COMMIT" > "$PACKAGE_DIR/source-commit"
# Contract 4 is a payload-only fast baseline. Candidate supplies its own already-built
# integration/browser runtimes when exercising these retained N-1 application images.
printf '%s\n' 4 > "$PACKAGE_DIR/qa-rollback-contract"

NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
"$NODE" - "$COMMIT" "$PACKAGE_DIR/integration-manifest.json" <<'NODE'
const fs = require("node:fs");
const [commit, output] = process.argv.slice(2);
const declaration = JSON.parse(fs.readFileSync(
  "examples/optional-observability/aetheus.integrations.json", "utf8"));
fs.writeFileSync(output, `${JSON.stringify({
  schema: 1,
  sourceCommit: commit,
  packages: declaration.packages
})}\n`);
NODE

docker save "$BACK_IMAGE" | gzip -1 > "$PACKAGE_DIR/aetheus-back.tar.gz"
docker save "$FRONT_IMAGE" | gzip -1 > "$PACKAGE_DIR/aetheus-front.tar.gz"
gzip -t "$PACKAGE_DIR/aetheus-back.tar.gz"
gzip -t "$PACKAGE_DIR/aetheus-front.tar.gz"

export DELIVERY_BASELINE_BOOTSTRAP=true
export DELIVERY_CANDIDATE_VERSION_MODE=source
"$NODE" deploy/scripts/generate-delivery-contract.mjs \
  "$PACKAGE_DIR/integration-manifest.json" \
  "$PACKAGE_DIR/delivery-contract.json" \
  aetheus 1 "c-$COMMIT" "$COMMIT" \
  src/Aetheus.Back/Data/Migrations \
  "$PACKAGE_DIR/aetheus-back.tar.gz" \
  "$PACKAGE_DIR/aetheus-front.tar.gz"

test -s "$PACKAGE_DIR/delivery-contract.json"
