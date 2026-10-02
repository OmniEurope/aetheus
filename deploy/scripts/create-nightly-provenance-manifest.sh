# SPDX-License-Identifier: EUPL-1.2
#
# Takes the browser image archive, restored whole, verifies every image archive is a sound gzip, and
# creates the nightly provenance manifest over them.
#
# The first three lines are the whole point: the nightly rebuilds from its own independent source, so
# it refuses unless the commit recorded in the artifacts is the commit this run was launched on.
#
# This was 24 lines inlined in .pipeline/aetheus-nightly.yaml. The body is relocated unchanged.
#
# Inputs, all read from the environment (none positional):
#   BUILD_SOURCEVERSION  the revision this nightly was launched on; the artifacts must carry it
set -eu
EXPECTED="${BUILD_SOURCEVERSION:-}"
ACTUAL="$(tr -d '\r\n' < .pipeline-artifacts/source-commit)"
test "$ACTUAL" = "$EXPECTED"
mv .nightly-browser/.pipeline-artifacts/aetheus-browser-smoke.tar.gz .pipeline-artifacts/aetheus-browser-smoke.tar.gz
gzip -t .pipeline-artifacts/aetheus-back.tar.gz
gzip -t .pipeline-artifacts/aetheus-front.tar.gz
gzip -t .pipeline-artifacts/aetheus-vitrine.tar.gz
gzip -t .pipeline-artifacts/aetheus-browser-smoke.tar.gz
rm -rf .nightly-evidence
mkdir -p .nightly-evidence
printf '%s\n' "$EXPECTED" > .nightly-evidence/source-commit
NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
"$NODE" deploy/scripts/nightly-evidence.mjs create \
  "$EXPECTED" .nightly-evidence/nightly-evidence.json \
  .pipeline-artifacts/aetheus-back.tar.gz \
  .pipeline-artifacts/aetheus-front.tar.gz \
  .pipeline-artifacts/aetheus-vitrine.tar.gz \
  .pipeline-artifacts/aetheus-browser-smoke.tar.gz
