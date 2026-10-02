# SPDX-License-Identifier: EUPL-1.2
#
# Refuses unless the checkout is exactly the revision this run was launched on, then makes sure the
# history the full secret scan needs is actually present.
#
# The unshallow is not optional in full mode: a shallow clone would make the scan report a clean
# history it never looked at, so it is fetched and then verified rather than assumed.
#
# This was 10 lines inlined in .pipeline/aetheus-security.yaml. The body is relocated unchanged.
#
# Inputs, all read from the environment (none positional):
#   BUILD_SOURCEVERSION    the revision this run was launched on
#   AETHEUS_GITLEAKS_MODE  `release-range` (default) or `full`; only `full` needs the whole history
set -eu
test "$(git rev-parse HEAD)" = "${BUILD_SOURCEVERSION}"
if [ "${AETHEUS_GITLEAKS_MODE:-release-range}" = full ] \
  && [ "$(git rev-parse --is-shallow-repository)" = true ]; then
  git fetch --unshallow origin
fi
if [ "${AETHEUS_GITLEAKS_MODE:-release-range}" = full ]; then
  test "$(git rev-parse --is-shallow-repository)" = false
fi
