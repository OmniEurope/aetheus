# SPDX-License-Identifier: EUPL-1.2
#
# Pins what this nightly is allowed to qualify, and proves its own evidence tooling still works
# before any of it is used.
#
# The branch and revision checks are the nightly's independence guarantee: it rebuilds from develop
# rather than reusing a candidate's artifacts, so a nightly launched on anything else would attest a
# qualification of something nobody asked about.
#
# This was 7 lines inlined in .pipeline/aetheus-nightly.yaml. The body is relocated unchanged.
#
# Inputs, all read from the environment (none positional):
#   BUILD_SOURCEBRANCH   must be develop
#   BUILD_SOURCEVERSION  the revision the checkout must be on
set -eu
test "${BUILD_SOURCEBRANCH:-}" = develop
test "$(git rev-parse HEAD)" = "${BUILD_SOURCEVERSION:-}"
STARTED_AT="$(date -u +%s)"
echo "##aetheus[setvariable name=NIGHTLY_STARTED_EPOCH]$STARTED_AT"
NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
"$NODE" --test deploy/scripts/nightly-evidence.test.mjs
